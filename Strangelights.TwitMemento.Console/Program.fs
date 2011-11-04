open Strangelights.TwitMemento
open System

let oauth_token, oauth_token_secret, usernameText = StoredOAuth.getOAuth (fun _ -> Console.ReadLine(), "robertpi")
let incoming = new TwitterStreamFollower(usernameText, 5, oauth_token, oauth_token_secret)
incoming.NewParsedTweet.Add (fun x -> printfn "%s %s" x.User.UserName x.Status)
incoming.StartListening()
Console.ReadLine() |> ignore
incoming.StopListening()
