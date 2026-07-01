namespace SandboxUploadApi

open System
open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features

open Giraffe
open FsToolkit.ErrorHandling

open Helpers

//----------------------------------------------------------------------------------
//!!! Copilot-assisted code, code review needed before releasing into production !!!
//----------------------------------------------------------------------------------

// Using Kestrel, not IIS here
module Handlers =   
    
    type private UploadError =
        | NoFormContent  of string
        | NoFileReceived of string
        | UploadFailed   of string
        | InvalidPath    of string  
        | NoSafeName     of string  
    
    let private sendResponse (statusCode: int) (message: string) (next: HttpFunc) (ctx : HttpContext) =

        async 
            {
                ctx.Response.StatusCode <- statusCode
                ctx.Response.ContentType <- "application/json"
                return! ctx.WriteJsonAsync({| message = message |}) |> Async.AwaitTask
            }
    
    let private getSafeFileName (formFile: IFormFile) =
        
        try
            let rawName =            
                match Option.ofNullEmptySpace formFile.Name with
                | Some name -> name
                | None      -> formFile.FileName
    
            let sanitized =
                try
                    rawName
                    |> Path.GetFileName
                    |> Option.ofNullEmptySpace
                    |> Option.defaultValue "upload_unknown.zip"
                    |> fun name 
                        ->
                        name.Trim()
                        |> Seq.map 
                            (fun c 
                                -> 
                                match c with
                                | c when Char.IsLetterOrDigit c || c = '.' || c = '-' || c = '_' -> c
                                | _ -> '_'
                            )
                        |> System.String.Concat
                with
                | _ -> rawName
    
            match Path.GetExtension(sanitized).ToLowerInvariant() with
            | ".zip" -> Ok sanitized
            | _      -> Ok <| sprintf "%s%s" sanitized ".zip"

        with
        | ex -> Error <| NoSafeName (string ex.Message) 
    
    let internal uploadHandler (uploadDir: string) : HttpHandler =

        fun (next: HttpFunc) (ctx: HttpContext)
            ->
            async
                {
                    let! result = 
                        asyncResult 
                            {
                                // 1. Configure Kestrel Request Limit
                                do! 
                                    match ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() |> Option.ofNull' with
                                    | Some feature
                                        ->
                                        feature.MaxRequestBodySize <- System.Nullable 500_000_000L
                                        Ok ()
                                    | None
                                        -> 
                                        Ok ()
    
                                // 2. Configure Multipart Form Size Limit
                                let formOptions = FormOptions(MultipartBodyLengthLimit = 500_000_000L)
                                ctx.Features.Set<IFormFeature>(FormFeature(ctx.Request, formOptions))
    
                                do! 
                                    ctx.Request.HasFormContentType
                                    |> Option.ofBool
                                    |> Option.toResult (NoFormContent "Expected multipart/form-data")
    
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
    
                    match result with
                    | Ok data 
                        ->
                        return! ctx.WriteJsonAsync data |> Async.AwaitTask

                    | Error (NoSafeName msg | NoFormContent msg)  
                        ->
                        return! sendResponse 415 msg next ctx    
    
                    | Error (NoFileReceived msg) 
                        ->
                        return! sendResponse 400 msg next ctx
    
                    | Error (UploadFailed msg | InvalidPath msg)
                        ->
                        eprintfn "Upload error: %s" msg
                        return! sendResponse 500 msg next ctx
                }

            |> Async.StartImmediateAsTask