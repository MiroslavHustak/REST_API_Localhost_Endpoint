namespace SandboxUploadApi

open System
open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features

open Giraffe
open Thoth.Json.Net 
open FsToolkit.ErrorHandling

open Helpers

//----------------------------------------------------------------------------------
// Copilot-assisted code, code review by a human performed on 02-10-2026
//----------------------------------------------------------------------------------

// Kestrel
module Handlers =   
    
    type private UploadError =
        | NoFormContent  of string
        | NoFileReceived of string
        | UploadFailed   of string
        | InvalidPath    of string  
        | NoSafeName     of string  
    
    let private sendResponse (statusCode: int) (message: string) (next: HttpFunc) (ctx : HttpContext) =

        let encodeError message : JsonValue =
            Encode.object
                [
                    "message", Encode.string message
                ]

        async 
            {
                ctx.Response.StatusCode <- statusCode
                ctx.Response.ContentType <- "application/json"
                
                let json =
                    encodeError message
                    |> Encode.toString 0                // 0 = compact output, 2 = indented
                       
                //return! ctx.WriteJsonAsync({| message = message |}) |> Async.AwaitTask
                return! ctx.WriteStringAsync json |> Async.AwaitTask  
            }
    
    let private getSafeFileName (formFile: IFormFile) =
        
        try       
            try
                formFile.FileName 
                |> Option.ofNullEmptySpace 
                |> Option.defaultValue formFile.FileName
                |> Path.GetFileName
                |> Option.ofNullEmptySpace
                |> Option.defaultValue "upload_unknown.zip"
                |> fun name 
                    ->
                    name.Trim()
                    |> Seq.map 
                        (fun c 
                            -> 
                            //[BCL] Char.IsLetterOrDigit = Unicode letters/digits (so "ž" or "ö" are allowed as well);
                            match c with
                            | c when Char.IsLetterOrDigit c || c = '.' || c = '-' || c = '_'
                                -> c
                            | _ -> '_'  //everything else (space, : \ / < > | ? * quotes, control chars...) becomes '_'
                        )
                    |> System.String.Concat
                    |> Ok
            with
            | ex -> Error <| NoSafeName (string ex.Message) 

            |> function
                | Ok sanitized 
                    ->    
                    match Path.GetExtension(sanitized).ToLowerInvariant() with
                    | ".zip" -> Ok sanitized
                    | _      -> Ok <| sprintf "%s%s" sanitized ".zip" //Only renaming, content is not verified to be a zip

                 | Error err
                     -> 
                     Error err  

        with
        | ex -> Error <| NoSafeName (string ex.Message) 
    
    // GIRAFFE 
    let internal uploadHandler (uploadDir: string) : HttpHandler =  

        fun (next: HttpFunc) (ctx: HttpContext)
            ->
            async
                {
                    let! result = 
                        asyncResult 
                            {
                                // [FEATURES] 
                                do!                                  
                                   // per-request Kestrel feature (null if the server does not support it)
                                   // https://learn.microsoft.com/en-us/aspnet/core/fundamentals/request-features?view=aspnetcore-10.0
                                    match ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() |> Option.ofNull' with
                                    | Some feature
                                        ->
                                        //Toto se bije s limitem v program.fs TODO: mrkni se na to
                                        feature.MaxRequestBodySize <- 1_000_000_000L //App-defined upload cap; Kestrel itself allows any value or null for unlimited
                                        Ok ()
                                    | None
                                        -> 
                                        Ok ()
    
                                let formOptions = FormOptions(MultipartBodyLengthLimit = 1_000_000_000L)  //ASP.NET Core's own form limit (default 128 MB), separate from Kestrel's
                                
                                // [FEATURES] replacing the request's form parser with one using our options,
                                ctx.Features.Set<IFormFeature>(FormFeature(ctx.Request, formOptions))
    
                                do! 
                                    ctx.Request.HasFormContentType
                                    |> Option.ofBool
                                    |> Option.toResult (NoFormContent "Expected multipart/form-data")
                                
                                // [UPLOADS] https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads
                                let! form =
                                    ctx.Request.ReadFormAsync()
                                    |> Async.AwaitTask
                                    |> Async.map Ok
    
                                let! file =
                                    match form.Files.Count with
                                    | 0 -> Error (NoFileReceived "No file received")
                                    | _ -> Ok (form.Files |> Seq.head)
    
                                let! fileName = getSafeFileName file
    
                                let destPath = Path.Combine(uploadDir, fileName)
    
                                let! fullDestPath =
                                    try
                                        let fullDest = Path.GetFullPath destPath
                                        let fullUploadDir = Path.GetFullPath uploadDir

                                        match fullDest.StartsWith(fullUploadDir, StringComparison.Ordinal) with
                                        | true  -> Ok fullDest
                                        | false -> Error (InvalidPath "Invalid file path - potential directory traversal")
                                    with
                                    | ex -> Error (InvalidPath <| sprintf "Path error: %s" (string ex.Message))
    
                                use fs = new FileStream(fullDestPath, FileMode.Create, FileAccess.Write, FileShare.None)
                                
                                do! file.CopyToAsync fs |> Async.AwaitTask
    
                                return
                                    {|
                                        message = "Upload successful"
                                        file = fileName
                                        sizeKb = fs.Length / 1024L
                                        savedTo = fullDestPath
                                    |}
                            }
                        |> AsyncResult.catch (fun ex -> UploadFailed <| string ex.Message)
    
                    match result with
                    | Ok data 
                        ->
                        return! ctx.WriteJsonAsync data |> Async.AwaitTask

                    | Error (NoSafeName msg | NoFormContent msg | NoFileReceived msg)  
                        ->
                        return! sendResponse 400 msg next ctx        
                      
                    | Error (UploadFailed msg | InvalidPath msg)
                        ->
                        eprintfn "Upload error: %s" msg
                        return! sendResponse 400 msg next ctx
                }

            |> Async.StartImmediateAsTask