namespace Helpers

open System

[<RequireQualifiedAccess>]            
module Result =    
          
    //Applicative functor      
    let internal sequence aListOfResults = //gets the first error - see the book Domain Modelling Made Functional
        let prepend firstR restR =
            match firstR, restR with
            | Ok first, Ok rest   -> Ok (first :: rest) | Error err1, Ok _ -> Error err1
            | Ok _, Error err2    -> Error err2
            | Error err1, Error _ -> Error err1

        let initialValue = Ok [] 
        List.foldBack prepend aListOfResults initialValue  

    let internal fromOption = 
        function   
        | Some value -> Ok value
        | None       -> Error String.Empty  

    let internal fromOption2 value err = 
        function   
        | Some _ -> Ok value
        | None   -> Error err   

    let internal toOption = 
        function   
        | Ok value -> Some value 
        | Error _  -> None  

    let internal fromBool ok err =                               
        function   
        | true  -> Ok ok  
        | false -> Error err

    let internal toBool =                               
        function   
        | Ok _    -> true  
        | Error _ -> false

    (*
    let defaultWith defaultFn res =
        match res with
        | Ok value  -> value
        | Error err -> defaultFn err 
        
    let defaultValue default res =
        match res with
        | Ok value -> value
        | Error _  -> default
        
    let map f res =
        match res with
        | Ok value  -> Ok (f value)
        | Error err -> Error err

    let mapError f res =
        match res with
        | Ok value  -> Ok value
        | Error err -> Error (f err)

    let bind f res =
        match res with
        | Ok value  -> f value
        | Error err -> Error err
    *)