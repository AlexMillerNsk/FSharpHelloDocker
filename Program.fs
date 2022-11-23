open System.Threading
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open System.IO
open System
open System.Net
open Suave.Sockets.Control
open Suave.Utils
open WebSocket
open Suave.Sockets
open System.Net.Sockets
open FSharp.Json
open Suave.Successful
open System.Threading

type AutorisationStatus = 
    |OpenAutorisation 
    |ClosedAutorisation

type MsgType =
    | SendMessage
    | AutorisationType of AutorisationStatus


type WsMessage =
    { MsgType: MsgType ; Message: string}
     static member Default = { MsgType = SendMessage; Message = "" }

type Subscriber = WebSocket * string

type State = {Subscribers:Subscriber list}

type Msg =
    | SendAll of ByteSegment
    | Subscribe of Subscriber
    | Unsubscribe of WebSocket

let msgMake x = x|>Json.serialize|> System.Text.Encoding.ASCII.GetBytes|> ByteSegment

let processor = MailboxProcessor<Msg>.Start(fun inbox ->
    let rec innerLoop state  = async {
        let! message = inbox.Receive()
        match message with
        | SendAll msg ->            
            for x in state.Subscribers do
                let ws = fst x
                let! result = ws.send Text msg true
                printfn "SendAll"
                ()              
            do! innerLoop state
        | Subscribe (x:Subscriber) ->
            let state = { state with Subscribers = x::state.Subscribers }           
            let listNames = state.Subscribers|>List.map (fun (x,y) -> y)|>string
            let msg = { MsgType = AutorisationType OpenAutorisation ; Message = listNames}|>msgMake           
            printfn $"{listNames}"
            for subscriber in state.Subscribers do
                let ws = fst subscriber
                let! result = ws.send Text msg true
                printfn "Send from Subscribe"
                ()    
            ()
            do! innerLoop state
        | Unsubscribe ws -> 
            let state = { state with Subscribers = state.Subscribers|>List.filter (fun (x,y) -> x <> ws) }
            let listNames = state.Subscribers|>List.map (fun (x,y) -> y)|>string
            let msg = { MsgType = AutorisationType ClosedAutorisation ; Message = listNames}|>msgMake
            printfn $"{listNames}"
            for subscriber in state.Subscribers do
                let wss = fst subscriber
                let! result = wss.send Text msg true
                printfn "Send from Unubscribe"
                ()   
            ()  
            do! innerLoop state
        ()
         }

    innerLoop {Subscribers=[]})               
let ws (webSocket : WebSocket) _ =
    socket {
        let mutable loop = true       
        while loop do
        let! msg = webSocket.read()
        match msg with
        | (Text, input, _) ->
            let text = ASCII.toString input 
            let deserializedText = Json.deserialize<WsMessage> text
            match deserializedText.MsgType with
            | SendMessage ->
                let byteResponse =
                    text
                    |> System.Text.Encoding.ASCII.GetBytes
                    |> ByteSegment
                printfn $"{text}"
                processor.Post(SendAll byteResponse )
                ()
            | AutorisationType autorisationStatus ->
                match autorisationStatus with
                | OpenAutorisation -> 
                    let name = deserializedText.Message
                    let newSubscriber:Subscriber = (webSocket,name)
                    processor.Post(Subscribe newSubscriber)
                    printfn $"{newSubscriber}"                   
                | _ -> printfn "error"
        | (Close, input, _) -> 
            printfn "good bye boi"
            processor.Post (Unsubscribe webSocket)
            processor.Post (SendAll (input|> ByteSegment ))
            let text = ASCII.toString input 
            printfn $"{text}"
            loop <- false
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
