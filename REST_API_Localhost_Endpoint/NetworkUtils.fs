module NetworkUtils 

open System
open System.Net
open System.Net.Sockets
 
open Helpers

let getLocalIPv4 () : string =  //try with je v main
        
    (*
    Socket --> an endpoint that establishes a direct TCP network connection between my PC and a remote web server (e.g., example.com on port 80).    
    Connection (socket.Connect(...)) --> a TCP handshake with uri.Host (resolving the IP address via DNS under the hood) on port 80.
    *)

    use socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    socket.Connect("8.8.8.8", 65530) // no packet actually sent; just resolves routing //"8.8.8.8" is an IP literal, so no DNS lookup happens; port 65530 is arbitrary
    
    (*
    //a type-test pattern (:?) never matches null, so the code shall be sufficient ...
    match socket.LocalEndPoint with
    | :? IPEndPoint as ep 
        -> ep.Address.ToString()  // vyjimecne ponechavam ToString(), bo nevim, jak to vnitrne je      
    | _ -> String.Empty
    *)

    // ... anyway, my paranoia and my coding guidelines override the aforementioned claim (this string comes from .NET anyway)
    socket.LocalEndPoint
    |> Option.ofNull'                                          
    |> Option.bind 
        (
            function
                | :? IPEndPoint as ep 
                    -> Some ep                       
                | _ -> None
        )
    |> Option.map (fun ep -> ep.Address.ToString() |> Option.ofNull') // vyjimecne ponechavam ToString(), bo nevim, jak to vnitrne je, jinak by stacil string ep.Address, coz automaticky zrusi null  
    |> Option.flatten
    |> Option.defaultValue String.Empty                        