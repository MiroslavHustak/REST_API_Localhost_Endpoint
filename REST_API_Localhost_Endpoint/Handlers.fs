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
// Copilot-assisted code; code review and complete revamp performed by a human on Oct 02, 2026
//----------------------------------------------------------------------------------

// Kestrel
module Handlers =   
    
    type private UploadError =
        | NoFormContent  of string
        | NoFileReceived of string
        | UploadFailed   of string
        | InvalidPath    of string  
        | NoSafeName     of string  

    let private encodeError (message: string) : JsonValue =
        Encode.object
            [
                "message", Encode.string message
            ]

    let private encodeSuccess (fileName: string) (sizeKb: int64) (savedTo: string) : JsonValue =
        Encode.object
            [
                "message", Encode.string "Upload successful"
                "file",    Encode.string fileName
                // Encode.int, NOT Encode.int64: Thoth encodes int64 as a JSON *string*. Upload cap is 1 GB, so KB always fits in int
                "sizeKb",  Encode.int (int sizeKb)
            ]

    let private sendJson (statusCode: int) (json: JsonValue) (ctx: HttpContext) =

        async 
            {
                ctx.Response.StatusCode  <- statusCode
                ctx.Response.ContentType <- "application/json"
                
                // 0 = compact output, 2 = indented
                return! ctx.WriteStringAsync (Encode.toString 0 json) |> Async.AwaitTask  
            }

    let private sendError (statusCode: int) (message: string) (ctx: HttpContext) =

        sendJson statusCode (encodeError message) ctx

    // file name sanitising    
    let private getSafeFileName (formFile: IFormFile) =
        
        try       
            formFile.FileName 
            |> Path.GetFileName                 // strips directory part; null stays null
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
            |> fun sanitized
                ->
                match Path.GetExtension(sanitized).ToLowerInvariant() with
                | ".zip" -> sanitized
                | _      -> sprintf "%s%s" sanitized ".zip" //Only renaming, content is not verified to be a zip
            |> Ok
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
                                        // Not a conflict with Program.fs: the global limit applies first, this per-request value
                                        // overrides it. The global one is redundant (or can be lowered to the default).
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

                                 // File to be uploaded
                                let! file =
                                    match form.Files.Count with
                                    | 0 -> Error (NoFileReceived "No file received")
                                    | _ -> Ok (form.Files |> Seq.head)
    
                                let! fileName = getSafeFileName file
    
                                // [BCL] join directory + sanitised name (no longer able to be rooted or contain separators)  
                                let destPath = Path.Combine(uploadDir, fileName)
    
                                let! fullDestPath = 
                                    try
                                        let fullDest = Path.GetFullPath destPath

                                        // TODO nekdy otestovat tuto kontrolu stylem PBT a pripadne vyuzit aji jinde 
                                        let fullUploadDir = 
                                            Path.GetFullPath uploadDir
                                            |> Path.TrimEndingDirectorySeparator
                                            |> fun dir -> dir + string Path.DirectorySeparatorChar

                                        // TODO nekdy otestovat tuto kontrolu stylem PBT a pripadne vyuzit aji jinde 
                                        match fullDest.StartsWith(fullUploadDir, StringComparison.Ordinal) with
                                        | true  -> Ok fullDest
                                        | false -> Error (InvalidPath "Invalid file path - potential directory traversal")
                                    with
                                    | ex -> Error (InvalidPath <| sprintf "Path error: %s" (string ex.Message))
    
                                // [BCL] `use` disposes at the end of the workflow. FileMode.Create = OVERWRITE existing file silently;
                                // FileShare.None = exclusive lock. (FileMode.CreateNew would fail instead of overwriting)
                                use fs = new FileStream(fullDestPath, FileMode.Create, FileAccess.Write, FileShare.None)
                                
                                // https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.http.iformfile
                                do! file.CopyToAsync fs |> Async.AwaitTask
    
                                // FIX: workflow returns the encoded JSON itself
                                return encodeSuccess fileName (fs.Length / 1024L) fullDestPath
                            }
                        |> AsyncResult.catch (fun ex -> UploadFailed <| string ex.Message)
    
                    match result with
                    | Ok json 
                        ->
                        return! sendJson 200 json ctx

                    | Error (NoSafeName msg | NoFormContent msg | NoFileReceived msg | InvalidPath msg)  
                        ->
                        return! sendError 400 msg ctx        
                      
                    | Error (UploadFailed msg)
                        ->
                        eprintfn "Upload error: %s" msg
                        return! sendError 500 "Upload failed" ctx
                }

            |> Async.StartImmediateAsTask