module NetworkUtils 

open System.Net
open System.Net.Sockets

let getLocalIPv4 () : string =
    
    use socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    socket.Connect("8.8.8.8", 65530) // no packet actually sent; just resolves routing
    
    match socket.LocalEndPoint with
    | :? IPEndPoint as ep 
        -> ep.Address.ToString()
    | _ -> "127.0.0.1"

