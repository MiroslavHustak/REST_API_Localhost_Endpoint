namespace SandboxUploadApi

open System
open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection

open Saturn
open Giraffe
open Thoth.Json.Net 

open Handlers
open Helpers
open ApiKeys.Secrets

//----------------------------------------------------------------------------------
// Copilot-assisted code, code review by a human performed on 06-10-2026
//----------------------------------------------------------------------------------

// KESTREL
module Program =  

    let private failFast (message: string) : 'a =

        eprintfn "FATAL: %s" message
        Console.WriteLine "Press any key to exit..."
        Console.ReadKey true |> ignore<ConsoleKeyInfo>
        exit 1

    [<EntryPoint>]
    let main args =

        let apiKey =
            
            async
                {   
                    // [BCL] AppContext.BaseDirectory = folder of the exe
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
                NetworkUtils.getLocalIPv4 () //returns the machine's local IPv4 as a non-null string ...
            with
            | ex -> failFast (sprintf "Could not determine local IPv4 address: %s" (string ex.Message))

            |> Option.ofNullEmptySpace //... ale String.Empty se tam vyskytnut moze
            |> Option.map (fun ip -> sprintf "%s%s:5000" @"http://" ip)
            |> Option.defaultWith (fun (_) -> failFast "Local IPv4 address resolved to empty — refusing to bind")

            // vyukova poznamka: kdybych pouzil defaultValue: defaultValue would take its fallback eagerly, 
            // which would mean failFast "Local IPv4 address resolved to empty..." got evaluated (and the process killed) on every run, Some/None regardless, since F# evaluates function arguments before applying
                           
        let encodeError message : JsonValue =
            Encode.object
                [
                    "message", Encode.string message
                ]

        // GIRAFFE
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
                        ctx.Response.ContentType <- "application/json; charset=utf-8"
        
                        let json =
                            encodeError "Unauthorized: Invalid API Key"
                            |> Encode.toString 0                // 0 = compact output, 2 = indented
                         
                        // return! ctx.WriteJsonAsync({| message = "Unauthorized: Invalid API Key" |}) 
                        // WriteJsonAsync (WriteJSONAsync obj takes an object) gets Json.ISerializer from DI (System.Text.Json by default in Giraffe 5+, Newtonsoft in older versions) 
                        // and serializes the anonymous record via runtime reflection --> Thoth encoders to avoid reflection.
                        return! ctx.WriteStringAsync json       // WriteSTRING... --> writes those bytes as-is
                }

        // SATURN   
        let apiRouter =

            router 
                {
                    pipe_through validateApiKey
                    post "/upload" (uploadHandler uploadDir)
                }

        // SATURN
        let app =

            application //configures and builds the web host
                {
                    use_router apiRouter
                    url localIp  //IIS hosting does not bind to IP/port inside the app, Saturn’s url setting binds Kestrel directly to a socket.
                    memory_cache
                    use_static "static"
                    use_gzip     //enables response compression middleware
                    host_config 
                        (fun hostBuilder //The whole block exists only to raise Kestrel's maximum request body size
                            ->
                            hostBuilder.ConfigureServices
                                (fun context services 
                                    ->
                                    services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>
                                        (fun (options: KestrelServerOptions) 
                                            ->
                                            //The default is about 30 MB (30,000,000 bytes)
                                            // A larger body is rejected with 413 Payload Too Large. 
                                            // options.Limits.MaxRequestBodySize <- Nullable() removes the limit entirely.
                                            options.Limits.MaxRequestBodySize <- 1_000_000_000L 
                                        ) 
                                    |> ignore<IServiceCollection>
                                )
                        )
                }
        
        // SATURN
        try
            run app
            0
        with
        | ex -> failFast (sprintf "Server terminated unexpectedly: %s" (string ex.Message))


        (*
        application
            {
            ... 
            }

        This CE in Saturn builds on top of Host.CreateDefaultBuilder/WebApplication under the hood. 
        That default host builder automatically registers the Console logging provider (Microsoft.Extensions.Logging.Console).

        Where the level is controlled: by default (no appsettings.json/appsettings.Production.json present, or with defaults inherited), 
        the minimum log level is Information, which is why Diagnostics[1]/[2] show up.
        If you want to quiet this down, you'd typically add an appsettings.json next to your exe like:
        
        json
        {
          "Logging": {
            "LogLevel": {
              "Default": "Warning",
              "Microsoft.AspNetCore.Hosting.Diagnostics": "None"
            }
          }
        }
        
        or configure it programmatically inside your host_config block via hostBuilder.ConfigureLogging(...), 
        the same place you're already tweaking KestrelServerOptions.       
        *)