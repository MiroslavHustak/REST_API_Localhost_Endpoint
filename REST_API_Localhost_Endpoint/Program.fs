namespace SandboxUploadApi

open System
open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection

open Saturn
open Giraffe

open Handlers
open ApiKeys.Secrets

//----------------------------------------------------------------------------------
//!!! Copilot-assisted code, code review needed before releasing into production !!!
//----------------------------------------------------------------------------------

module Program =  //Kestrel

    [<EntryPoint>]
    let main args =

        let apiKey =

            let apiKeySecretsPath = Path.Combine(AppContext.BaseDirectory, "Secrets", "secrets.json")

            match loadApiKey apiKeySecretsPath with
            | Ok secrets 
                when not (String.IsNullOrWhiteSpace secrets.ApiKey) 
                -> 
                secrets.ApiKey
            | _ 
               ->
                eprintfn "FATAL: Could not load API key from secrets.json — refusing to start"
                exit 1

        let uploadDir = Path.Combine(AppContext.BaseDirectory, "uploads")
        Directory.CreateDirectory uploadDir |> ignore

        let validateApiKey (next: HttpFunc) (ctx: HttpContext) =

            task
                {
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
                    url "http://192.168.1.11:5000"  //IIS hosting does not bind to IP/port inside the app, Saturn’s url setting binds Kestrel directly to a socket.
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

        run app

        0