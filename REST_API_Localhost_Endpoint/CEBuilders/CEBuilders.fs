namespace Helpers

module Builders =
        
    type internal MyBuilder = MyBuilder with //This CE builder is a monad-style control-flow helper, not a monad
        member _.Recover(m : bool * (unit -> 'a), nextFunc : unit -> 'a) : 'a =
            match m with
            | (false, handleFalse)
                -> handleFalse()
            | (true, _)
                -> nextFunc()    
        member this.Bind(m, f) = this.Recover(m, f) //an alias to prevent confusion      
        member _.Return x : 'a = x   
        member _.ReturnFrom x : 'a = x       
        member _.Using(resource, binder) =
            use r = resource
            binder r  
      
    let internal pyramidOfHell = MyBuilder

    //**************************************************************************************
   
    type Builder2 = Builder2 with    // This CE builder is a monad-style control-flow helper, not a lawful monad
        member _.Recover((m, recovery), nextFunc) =
            match m with
            | Some v -> nextFunc v
            | None   -> recovery    
        member this.Bind(m, f) = this.Recover(m, f) //an alias to prevent confusion        
        member _.Return x : 'a = x   
        member _.ReturnFrom x : 'a = x       
        member _.Using(resource, binder) =
            use r = resource
            binder r
        
    let internal pyramidOfDoom = Builder2
    
    //**************************************************************************************
       
    type internal MyBuilder3 = MyBuilder3 with  // This CE builder is a monad-style control-flow helper, not a lawful monad
        member _.Recover(m, nextFunc) = 
            match m with
            | (Ok v, _)           
                -> nextFunc v 
            | (Error err, handler) 
                -> handler err
        member this.Bind(m, f) = this.Recover(m, f) //an alias to prevent confusion        
        member _.Return x : 'a = x   
        member _.ReturnFrom x : 'a = x       
        member _.Using(resource, binder) =
            use r = resource
            binder r   
        
    let internal pyramidOfInferno = MyBuilder3  // A reinvented result {} CE with Result.mapError, already refactored in the code

    //**************************************************************************************

    type internal MyBuilder5 = MyBuilder5 with   // This CE builder is a monad-style control-flow helper, not a lawful monad
         member _.Recover(m : bool * 'a, nextFunc : unit -> 'a) : 'a =
             match m with
             | (false, value)
                 -> value
             | (true, _)
                 -> nextFunc() 
         member this.Bind(m, f) = this.Recover(m, f) //an alias to prevent confusion              
         member _.Return x : 'a = x   
         member _.ReturnFrom x : 'a = x       
         member _.Using(resource, binder) =
             use r = resource
             binder r

    let internal pyramidOfDamnation = MyBuilder5

    //**************************************************************************************
    type internal OptionAdaptedBuilder2 = OptionAdaptedBuilder2 with
        member _.Bind(m, nextFunc) =
            match m with
            | Some v -> nextFunc v
            | None   -> None    
        member _.Return x : 'a = x   
        member _.ReturnFrom x : 'a = x      
        member _.Using(resource, binder) =
            use r = resource
            binder r
    
    let internal option2 = OptionAdaptedBuilder2
      
    //**************************************************************************************
    type internal OptionAdaptedBuilder = OptionAdaptedBuilder with
        member _.Bind(m, nextFunc) =
            match m with
            | true  -> nextFunc() 
            | false -> false    
        member _.Return x : 'a = x   
        member _.ReturnFrom x : 'a = x
        member _.Zero() : bool = false
        member _.Delay(f : unit -> bool) = f
        member _.Run(f) = f()
        member _.Using(resource, binder) =
            use r = resource
            binder r
    
    let internal optionBool = OptionAdaptedBuilder
      
    //**************************************************************************************

    type internal Reader<'e, 'a> = 'e -> 'a
    
    type internal ReaderBuilder = ReaderBuilder with
        member __.Bind(m, f) = fun env -> f (m env) env      
        member __.Return x = fun _ -> x
        member __.ReturnFrom x = x
  
    let internal reader = ReaderBuilder 

    //**************************************************************************************

    type RealWorld = RealWorldToken
    
    // The IO monad: a function that takes a RealWorld token and returns a new one plus a value
    type IO_Monad<'a> = IO_Monad of (RealWorld -> RealWorld * 'a)

    type IOMonad = IOMonad with

        member _.Bind(io : IO_Monad<'a>, binder : 'a -> IO_Monad<'b>) : IO_Monad<'b> =
            IO_Monad 
                (fun world 
                    ->
                    let runIO_helper (IO_Monad f) = f
                    let world', a = runIO_helper io world
                    runIO_helper (binder a) world'
                )

        member _.Delay(f : unit -> IO_Monad<'a>) : IO_Monad<'a> =
            IO_Monad 
                (fun world
                    ->
                    let (IO_Monad delayed) = f()
                    delayed world
                )
        
        // For do! notation with sequencing (ignoring result)
        member this.Combine(io1 : IO_Monad<unit>, io2 : IO_Monad<'a>) : IO_Monad<'a> =
            this.Bind(io1, fun () -> io2)
            
        member _.Zero() : IO_Monad<unit> = 
            IO_Monad (fun world -> world, ())

        member _.Return(x) : IO_Monad<'a> =
            IO_Monad (fun world -> world, x)
        
        member _.ReturnFrom(io : IO_Monad<'a>) = io
        
    let internal IOMonad = IOMonad

   //**************************************************************************************

    let [<Literal>] MinLengthCE = 2 //Builder pro CE vyzaduje velke pismeno.... strange
    let [<Literal>] MaxLengthCE = 3 
       
    type internal XorBuilder = XorBuilder with
    
        member _.Yield(value : bool) = [ value ]
        member _.Combine(previous : bool list, following : bool list) = previous @ following
        member _.Delay(func : unit -> bool list) = func() 
    
        member _.Run(values : bool list) =   

            match values.Length with
            | MinLengthCE
                ->
                let a = values |> List.item 0
                let b = values |> List.item 1
                Ok ((a && not b) || (not a && b)) // XOR logic for 2 values

            | MaxLengthCE 
                ->
                let a = values |> List.item 0
                let b = values |> List.item 1
                let c = values |> List.item 2
                Ok ((a && not b && not c) || (not a && b && not c) || (not a && not b && c)) // XOR logic for 3 values

            | _ ->
                Error "Invalid number of values for XOR computation"
    
        member _.Zero() = []

    let internal xor = XorBuilder


    // ************** Educational code *****************
    // Simplified library definition code for result {}, asyncResult {}, asyncOption {} and option {} CE builders
    // Pouze pro ukazku monadickeho Bind (kompletni kod v FsToolkit.ErrorHandling je rozsahlejsi)
 
    type ResultBuilder = ResultBuilder with 
        member _.Bind(m : Result<'a,'e>, f : 'a -> Result<'b,'e>) : Result<'b,'e> =
            match m with
            | Ok v -> f v
            | Error e -> Error e
        member _.Return(x : 'a) : Result<'a,'e> = Ok x
        member _.ReturnFrom(x : Result<'a,'e>) : Result<'a,'e> = x
        member _.Zero() : Result<unit,'e> = Ok ()
        member _.Delay(f : unit -> Result<'a,'e>) = f()
        member _.Using(resource : #System.IDisposable, binder : #System.IDisposable -> option<'a>) =
            use r = resource
            binder r

    type OptionBuilder = OptionBuilder with
        member _.Bind(m : option<'a>, f : 'a -> option<'b>) : option<'b> =
                  match m with
                  | Some v -> f v
                  | None -> None
        member _.Return(x : 'a) : option<'a> = Some x    
        member _.ReturnFrom(x : option<'a>) : option<'a> = x     
        member _.Zero() : option<unit> = None
        member _.Delay(f : unit -> option<'a>) = f()
        member _.Using(resource : #System.IDisposable, binder : #System.IDisposable -> option<'a>) =
            use r = resource
            binder r

    type AsyncResultBuilder = AsyncResultBuilder with    
        member _.Bind(m : Async<Result<'a,'e>>, f : 'a -> Async<Result<'b,'e>>) : Async<Result<'b,'e>> =
            async
                {
                    let! res = m
                    match res with
                    | Ok v -> return! f v
                    | Error e -> return Error e
                }
        member _.Return(x : 'a) : Async<Result<'a,'e>> = async { return Ok x }           
        member _.ReturnFrom(x : Async<Result<'a,'e>>) : Async<Result<'a,'e>> = x  
        member _.Zero() : Async<Result<unit,'e>> = async { return Ok () }
        member _.Delay(f : unit -> Async<Result<'a,'e>>) : Async<Result<'a,'e>> = async.Delay f
        member _.Combine(r1 : Async<Result<unit,'e>>, r2 : Async<Result<'a,'e>>) : Async<Result<'a,'e>> =
            async 
                {
                    let! res = r1
                    match res with
                    | Ok () -> return! r2
                    | Error e -> return Error e
                }    
        member _.TryWith(body : unit -> Async<Result<'a,'e>>, handler : exn -> Async<Result<'a,'e>>) =
            async 
                {
                    try return! body()
                    with
                    | ex -> return! handler ex
                }    
        member _.TryFinally(body : unit -> Async<Result<'a,'e>>, compensation : unit -> unit) =
            async
                {
                    try return! body()
                    finally compensation()
                }    
        member _.Using(resource : #System.IDisposable, binder : #System.IDisposable -> Async<Result<'a,'e>>) =
            async 
                {
                    use r = resource
                    return! binder r
                }
    
        // optional helper for mapping synchronous results inside the CE
        member _.BindReturn(m : Async<Result<'a,'e>>, f : 'a -> 'b) : Async<Result<'b,'e>> =
            async 
                {
                    let! res = m
                    match res with
                    | Ok v -> return Ok (f v)
                    | Error e -> return Error e
                }

    type AsyncOptionBuilder = AsyncOptionBuilder with
        member _.Bind(m : Async<option<'a>>, f : 'a -> Async<option<'b>>) : Async<option<'b>> =
            async
                {
                    let! res = m
                    match res with
                    | Some v -> return! f v
                    | None -> return None
                }
        member _.Return(x : 'a) : Async<option<'a>> = async { return Some x }
        member _.ReturnFrom(x : Async<option<'a>>) : Async<option<'a>> = x
        member _.Zero() : Async<option<unit>> = async { return Some () }    
        member _.Delay(f : unit -> Async<option<'a>>) = async.Delay f    
        member _.Combine(a : Async<option<unit>>, b : Async<option<'a>>) =
            async
                {
                    let! r = a
                    match r with
                    | Some () -> return! b
                    | None -> return None
                }    
        member _.TryWith(body : unit -> Async<option<'a>>, handler : exn -> Async<option<'a>>) =
            async 
                {
                    try return! body()
                    with 
                    | ex -> return! handler ex
                }    
        member _.TryFinally(body : unit -> Async<option<'a>>, compensation : unit -> unit) =
            async 
                {
                    try return! body()
                    finally compensation()
                }    
        member _.Using(resource : #System.IDisposable, binder : #System.IDisposable -> Async<option<'a>>) =
            async 
                {
                    use r = resource
                    return! binder r
                }