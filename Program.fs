
open System.Threading
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open System.IO
open System
open Suave.Sockets.Control
open Suave.Utils
open WebSocket
open Suave.Sockets
open Npgsql.FSharp
open Fable.Remoting.Server
open Fable.Remoting.Suave
open Shared


let connectionString : string =
    Sql.host "130.193.52.90"
    |> Sql.database "test_db"
    |> Sql.username "testuser"
    |> Sql.password "d42299d5"
    |> Sql.port 5432
    |> Sql.formatConnectionString



let readCars (connectionString: string) : Car list =
    connectionString
    |> Sql.connect
    |> Sql.query "SELECT * FROM cars"
    |> Sql.execute (fun read ->
        {
            Name = read.text "name"
            Url = read.text "url"
        })


//for user in users do
//    printfn "User(%d) -> {%s}" user.Id user.Username

let carsStore : ICarsStore = {
    carsfrombase = readCars connectionString
}


type State = {Subscribers: WebSocket list}

type Msg =
    | SendAll of ByteSegment
    | Subscribe of WebSocket

    



let processor = MailboxProcessor<Msg>.Start(fun inbox ->
    let rec innerLoop state  = async {
        let! message = inbox.Receive()
        match message with
        | SendAll msg ->            
            for x in state.Subscribers do
                let! result = x.send Text msg true
                printfn "SendAll"
                ()              
            do! innerLoop state
        | Subscribe ws ->
            let state = { state with Subscribers = ws::state.Subscribers }             
            do! innerLoop state
        ()
         }

    innerLoop {Subscribers=[]})
                  
let ws (webSocket : WebSocket) _ =
    processor.Post(Subscribe webSocket)
    socket {
        let mutable loop = true       
        while loop do
        let! msg = webSocket.read()
        match msg with
        | (Text, input, _) ->
            let text = ASCII.toString input
            let byteResponse =
                text
                |> System.Text.Encoding.ASCII.GetBytes
                |> ByteSegment
            processor.Post(SendAll byteResponse )
            printfn $"ws{text}"
        | (Close, input, _) -> 
            printfn "good bye boi"
            processor.Post (SendAll (input|> ByteSegment ))
            let text = ASCII.toString input 
            printfn $"{text}"
            loop <- false
        | _ -> ()
           }
let fableWebApi : WebPart = 
    Remoting.createApi()
    |> Remoting.fromValue carsStore
    |> Remoting.buildWebPart

[<EntryPoint>]
let main argv =
  let app =
    choose
      [ fableWebApi
        GET >=> choose
          [ path "/hello" >=> OK "helo get"
            path "/" >=> Files.browseFileHome "index.html" 
            path "/bundle.js" >=> Files.browseFileHome "bundle.js" 
            GET >=> Files.browseHome
            path "/websocket" >=> handShake ws
            RequestErrors.NOT_FOUND "Page not found." 
            path "/goodbye" >=> OK "Good bye GET" ]
        POST >=> choose
          [ path "/hello" >=> OK "Hello POST"
            path "/goodbye" >=> OK "Good bye POST" ] ]
  let cts = new CancellationTokenSource()
  let portEnvVar = Environment.GetEnvironmentVariable "PORT"
  let port = if String.IsNullOrEmpty portEnvVar then 8080 else (int)portEnvVar
  let conf = { defaultConfig with cancellationToken = cts.Token
                                  bindings = [HttpBinding.createSimple HTTP "0.0.0.0" port ]
                                  homeFolder = Some(Path.GetFullPath "./Public") }
  printfn $"port is {portEnvVar}"
  startWebServer conf app
  0
