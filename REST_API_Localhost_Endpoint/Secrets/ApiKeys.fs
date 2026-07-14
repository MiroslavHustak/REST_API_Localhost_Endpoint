module ApiKeys

open System
open System.IO

open Thoth.Json.Net
open FsToolkit.ErrorHandling

open Helpers

type Secrets =
    {
        ApiKey: string
    }

module Secrets =

    let private decoder : Decoder<Secrets> =
        Decode.object
            (fun get
                ->
                {
                    ApiKey = get.Required.Field "ApiKey" Decode.string
                }
            )
               
    let internal loadApiKeyAsync (path: string) =

          asyncResult
              {
                  let! fullPath = 
                      Path.Combine(AppContext.BaseDirectory, path) 
                      |> Option.ofNullEmptySpace //AppContext.BaseDirectory always points to where your compiled app lives, regardless of what the process working directory happens to be
                      |> Option.toResult (sprintf "Failed to read secrets file")

                  let! json = 
                      System.IO.File.ReadAllTextAsync fullPath 
                      |> Option.ofNullEmptySpace
                      |> Option.toResult (sprintf "Failed to read secrets file")

                  return! Decode.fromString decoder json
              }
          |> AsyncResult.catch (fun ex -> sprintf "Failed to read secrets file: %s" (string ex.Message))       

    (*
    Do <ItemGroup>
    pridej
    <Content Update="Secrets\secrets.json">
    	<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>    
    *)