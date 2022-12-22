
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



let connectionString : string =
    Sql.host "130.193.52.90"
    |> Sql.database "test_db"
    |> Sql.username "testuser"
    |> Sql.password "d42299d5"
    |> Sql.port 5432
    |> Sql.formatConnectionString

type User = {
    Id: int;
    Username: string;
    Age: int
}

let readUsers (connectionString: string) : User list =
    connectionString
    |> Sql.connect
    |> Sql.query "SELECT * FROM users"
    |> Sql.execute (fun read ->
        {
            Id = read.int "id"
            Username = read.text "name"
            Age = read.int "age"
        })

let addUser (connectionString: string, name:string, age:int) : int =
    connectionString
    |> Sql.connect
    |> Sql.query "INSERT INTO users (name, age) VALUES (@name, @age)"
    |> Sql.parameters [ "@name", Sql.text name; "@age", Sql.int age ]
    |> Sql.executeNonQuery


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
            addUser (connectionString, text, 15)|>ignore
        | (Close, input, _) -> 
            printfn "good bye boi"
            processor.Post (SendAll (input|> ByteSegment ))
            let text = ASCII.toString input 
            printfn $"{text}"
            loop <- false
            let users = readUsers connectionString
            for user in users do
                printfn "User(%d) -> {%s}" user.Id user.Username
        | _ -> ()
           }


[<EntryPoint>]
let main argv =
  let app =
    choose
      [ GET >=> choose
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
