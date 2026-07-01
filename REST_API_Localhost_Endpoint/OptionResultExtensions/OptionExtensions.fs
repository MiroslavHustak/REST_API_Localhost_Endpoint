namespace Helpers

open System

open FsToolkit.ErrorHandling

//***********************************

open Helpers.Builders      

[<RequireQualifiedAccess>]            
module Option =

    let internal ofBool =                           
        function   
        | true  -> Some ()  
        | false -> None

    let internal toBool = 
        function   
        | Some _ -> true
        | None   -> false

    let internal fromBool value =                               
        function   
        | true  -> Some value  
        | false -> None

    let internal fromBool' (f : 'a -> 'b) =
        function
        | true  -> Some f
        | false -> None

    let inline internal ofNull' (value : 'nullableValue) =
        match System.Object.ReferenceEquals(box value, null) with //boxing a null Nullable<'T> produces an actual null reference
        | true  -> None
        | false -> Some value   

    let inline internal ofPtrOrNull (value : 'nullableValue) =  
        let boxedValue = box value  
        
        match System.Object.ReferenceEquals(boxedValue, null) with 
        | true  ->
                None
        | false -> 
                match boxedValue with
                | null 
                    -> None
                | :? IntPtr as ptr 
                    when ptr = IntPtr.Zero
                    -> None
                | _ -> Some value          
    
    let inline internal ofNullEmpty (value : 'nullableValue) : string option = //NullOrEmpty
        option 
            {
                let!_ = (not <| System.Object.ReferenceEquals(box value, null)) |> fromBool value  
                let value = string value 
                let! _ = (not <| String.IsNullOrEmpty value) |> fromBool value  //IsNullOrEmpty is not for nullable types
                return value
            }

    let inline internal ofNullEmpty2 (value : 'nullableValue) : string option =
        option2 
            {
                let!_ = (not <| System.Object.ReferenceEquals(box value, null)) |> fromBool value                            
                let value : string = string value
                let!_ = (not <| String.IsNullOrEmpty value) |> fromBool value

                return Some value
            }

    let inline internal ofNullEmptySpace (value : 'nullableValue) = //NullOrEmpty, NullOrWhiteSpace
        pyramidOfDoom //nelze option {}
            {
                let!_ = (not <| System.Object.ReferenceEquals(box value, null)) |> fromBool Some, None 
                let value = string value 
                let! _ = (not <| String.IsNullOrWhiteSpace value) |> fromBool Some, None
       
                return Some value
            }

    let internal toResult err = 
        function   
        | Some value -> Ok value 
        | None       -> Error err  

    (*
    let internal ofNullEmpty2 (value : string) : string option =
        option 
            {
                do! (not <| System.Object.ReferenceEquals(value, null)) |> fromBool value                            
                let value : string = string value
                do! (not <| String.IsNullOrEmpty value) |> fromBool value

                return value
            } 
    *)

    (*
    let defaultValue default opt =
        match opt with
        | Some value -> value
        | None       -> default
        
    let map f opt =
        match opt with
        | Some value -> Some (f value)
        | None       -> None

    let bind f opt =
        match opt with
        | Some value -> f value
        | None       -> None

    let orElseWith (f: unit -> 'T option) (option: 'T option) : 'T option =
        match option with
        | Some x -> Some x
        | None   -> f()
           
    let iter action option =
        match option with
        | Some x -> action x
        | None   -> () 
    *) 

    (*
        monadic composition (>>=) in Haskell

        import Control.Monad (guard)

        validate :: Maybe String -> Maybe String
        validate value = 
        value >>= \v ->                      -- Check if value is Just
        guard (not (null v)) >> Just v       -- Check if value is not empty, return Just v
        
        //*****************************************
        
        do notation

        import Control.Monad (guard)
    
        validate :: Maybe String -> Maybe String
        validate value = do
            v <- value                    -- Check if value is Just
            guard (not (null v))          -- Equivalent to `let! _ = not <| String.IsNullOrEmpty(value), None`
            return v 
    *)