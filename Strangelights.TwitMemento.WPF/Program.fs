module Program
open System
open System.Windows
open Strangelights.TwitMemento

// The applications main entry
[<STAThread; EntryPoint>]
let main args =
    // use the view's main window's load event to wire up some stuff
    View.MainWindow.Loaded.Add(fun _ ->
        // twitter needs this to be false, don't know why
        System.Net.ServicePointManager.Expect100Continue <- false

        // clear storage
        //Storage.reset()

        // get the oauth token from the user
        let oauth_token, oauth_token_secret, username = View.createOAuthWindow()
        
        // create our streamer object
        let incoming = new TwitterStreamFollower(username, 5, oauth_token, oauth_token_secret)
        // add the stuff we need to do when a new tweet comes in
        incoming.NewParsedTweet.Add(fun x -> 
            ViewModel.AllTweetsOC.Add(x)
            View.AllTweets.ScrollToBottom())
        // add the stuff we need to do when converstation list is updated
        incoming.ConversationsUpdate.Add ViewModel.treatConversations
        
        // add when we close down close the listen (not convinced this is working)
        View.MainWindow.Closing.Add(fun _ -> incoming.StopListening())

        // start listen to that twitter stream
        incoming.StartListening())
    
    // start event loop and show our window
    let app = new Application()
    app.Run(View.MainWindow)