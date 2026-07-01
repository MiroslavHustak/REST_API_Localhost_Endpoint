module ApiKeys

open System
open System.IO

open Thoth.Json.Net
open FsToolkit.ErrorHandling

open Helpers
   
type [<Struct>] internal IO<'a> = IO of (unit -> 'a) // wrapping custom type simulating Haskell's IO Monad (without the monad, of course)

let internal runIO (IO action) = action () 
let internal runIOAsync (IO action) : Async<'a> = async { return action () }
 
type Secrets =
    {
        ApiKey : string
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

    let internal loadApiKey (path : string) : Result<Secrets, string> =
        try
            let fullPath = Path.Combine(AppContext.BaseDirectory, path) //AppContext.BaseDirectory always points to where your compiled app lives, regardless of what the process working directory happens to be
            let json = System.IO.File.ReadAllText fullPath
            Decode.fromString decoder json
        with
        | ex -> Error (sprintf "Failed to read secrets file: %s" (string ex.Message))

    (*
    Do <ItemGroup>
    pridej
    <Content Update="Secrets\secrets.json">
    	<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>    
    *)