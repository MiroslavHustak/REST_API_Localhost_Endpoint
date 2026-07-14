namespace SandboxUploadApi

open System
open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection

open Saturn
open Giraffe

open Handlers
open Helpers
open ApiKeys.Secrets

//----------------------------------------------------------------------------------
//!!! Copilot-assisted code, code review needed before releasing into production !!!
//----------------------------------------------------------------------------------

module Program =  //Kestrel

    let private failFast (message: string) : 'a =
        eprintfn "FATAL: %s" message
        Console.WriteLine "Press any key to exit..."
        Console.ReadKey true |> ignore
        exit 1

    [<EntryPoint>]
    let main args =

        let apiKey =
            
            async
                {
                    let apiKeySecretsPath = Path.Combine(AppContext.BaseDirectory, "Secrets", "secrets.json")

                    match! loadApiKeyAsync apiKeySecretsPath with
                    | Ok secrets 
                        when not (String.IsNullOrWhiteSpace secrets.ApiKey) 
                        -> return secrets.ApiKey
                    | _ -> return failFast "Could not load API key from secrets.json — refusing to start"
                }

        let uploadDir = Path.Combine(AppContext.BaseDirectory, "uploads")

        try
            Directory.CreateDirectory uploadDir |> ignore<DirectoryInfo>
        with
        | ex -> failFast (sprintf "Could not create upload directory '%s': %s" uploadDir (string ex.Message))

        let localIp =
        
            try
                NetworkUtils.getLocalIPv4 ()
            with
            | ex -> failFast (sprintf "Could not determine local IPv4 address: %s" (string ex.Message))

            |> Option.ofNullEmptySpace
            |> Option.map (fun ip -> sprintf "%s%s:5000" @"http://" ip)
            |> Option.defaultWith (fun (_) -> failFast "Local IPv4 address resolved to null/empty — refusing to bind")

            // vyukova poznamka, kdybych pouzil defaultValue: defaultValue would take its fallback eagerly, 
            // which would mean failFast "Local IPv4 address resolved to null/empty..." got evaluated (and the process killed) on every run, Some/None regardless, since F# evaluates function arguments before applying

        let validateApiKey (next: HttpFunc) (ctx: HttpContext) =

            task
                {
                    let! apiKey = apiKey |> Async.StartAsTask

                    match ctx.Request.Headers.TryGetValue "X-API-KEY" with
                    | true, key 
                        when string key = apiKey
                        ->
                        return! next ctx
                    | _ ->
                        ctx.Response.StatusCode  <- 401
                        ctx.Response.ContentType <- "application/json"
                        return! ctx.WriteJsonAsync({| message = "Unauthorized: Invalid API Key" |})
                }

        let apiRouter =

            router 
                {
                    pipe_through validateApiKey
                    post "/upload" (uploadHandler uploadDir)
                }

        let app =

            application 
                {
                    use_router apiRouter
                    url localIp  //IIS hosting does not bind to IP/port inside the app, Saturn’s url setting binds Kestrel directly to a socket.
                    memory_cache
                    use_static "static"
                    use_gzip
                    host_config 
                        (fun hostBuilder
                            ->
                            hostBuilder.ConfigureServices
                                (fun context services 
                                    ->
                                    services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>
                                        (fun (options: KestrelServerOptions) 
                                            ->
                                            options.Limits.MaxRequestBodySize <- System.Nullable 500_000_000L
                                        ) 
                                    |> ignore<IServiceCollection>
                        )
                    )
                }

        try
            run app
            0
        with
        | ex -> failFast (sprintf "Server terminated unexpectedly: %s" (string ex.Message))