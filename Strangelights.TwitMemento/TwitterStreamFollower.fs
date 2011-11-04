namespace Strangelights.TwitMemento
open Strangelights.TwitMemento
open Strangelights.TwitMemento.WebRequestExt
open System
open System.Globalization
open System.IO
open System.Net
open System.Web
open System.Threading
open Microsoft.FSharp.Control.WebExtensions
open System.Xml.Linq
open System.Text.RegularExpressions
open System.Runtime.Serialization
open System.Runtime.Serialization.Json


[<AutoOpen>]
module Misc =
    let debug = true

    type SynchronizationContext with
        /// A standard helper extension method to raise an event on the GUI thread
        member syncContext.RaiseEvent (event: Event<_>) args =
            syncContext.Post((fun _ -> event.Trigger args),state=null)
 
        /// A standard helper extension method to capture the current synchronization context.
        /// If none is present, use a context that executes work in the thread pool.
        static member CaptureCurrent () =
            match SynchronizationContext.Current with
            | null -> new SynchronizationContext()
            | ctxt -> ctxt

    let twitterUserName = new Regex("@[_A-Za-z]+")

    let xn (s:string) = XName.op_Implicit s

    // case insentative string equality
    let (=.) s1 s2 =
        String.Compare(s1, s2, StringComparison.InvariantCultureIgnoreCase) = 0

    // case insentative string inequality
    let (<>.) s1 s2 = not  (s1 =. s2) 



module StoredOAuth =
    // Compute URL to send user to to allow our app to connect with their credentials,
    // then open the browser to have them accept
    let getOAuth pinProvider =
        let doSecondHalf oauth_token'' oauth_token_secret'' =
            let pin, username = pinProvider()
            let oauth_token, oauth_token_secret = OAuth.accessToken oauth_token'' oauth_token_secret'' pin
            Storage.storeKeys oauth_token oauth_token_secret
            Storage.storePinAndUsername pin username
            printfn "oauth_token = %s oauth_token_secret =  %s pin = %s"  oauth_token oauth_token_secret pin
            oauth_token, oauth_token_secret, username
        match Storage.readKeys() with
        | None, None, None, None ->
            let oauth_token'', oauth_token_secret'', oauth_callback_confirmed = OAuth.requestToken()
            let url = OAuth.authorizeURI + "?oauth_token=" + oauth_token''
            Storage.storeKeys oauth_token'' oauth_token_secret''
            System.Diagnostics.Process.Start(url) |> ignore
            doSecondHalf oauth_token'' oauth_token_secret''
        | Some oauth_token'', Some oauth_token_secret'', None, None ->
            doSecondHalf oauth_token'' oauth_token_secret''
        | Some oauth_token, Some oauth_token_secret, Some pin, Some username ->
            oauth_token, oauth_token_secret, username
        | _ -> failwith "asset false"

module OtherTwitterStuff =
    let updateStatus status =
        let oauth_token, oauth_token_secret =
            match Storage.readKeys() with
            | Some oauth_token, Some oauth_token_secret, Some _, Some _ ->
                oauth_token, oauth_token_secret
            | _ -> failwith "asset false"
        let statusUrl = "https://api.twitter.com/1/statuses/update"
        let request = WebRequest.Create (statusUrl, Method="POST")
        let tweet =  OAuth.urlEncode(status)
        request.AddOAuthHeader(oauth_token,oauth_token_secret,["status",tweet])
        let doWriteBody() =
            use reqStream = request.GetRequestStream() 
            use streamWriter = new StreamWriter(reqStream)
            streamWriter.Write(sprintf "status=%s" tweet)
        doWriteBody()
        use resp = 
            try
                request.GetResponse()
            with
            | :? WebException as ex -> 
                use resp = ex.Response
                use strm = resp.GetResponseStream()
                let text = (new StreamReader(strm)).ReadToEnd()
                if debug then printfn "%s" text
                reraise()
            | _ -> 
                reraise()
        use strm = resp.GetResponseStream()
        let text = (new StreamReader(strm)).ReadToEnd()
        text

module JsonParsering =
    open System
    open System.Xml.Linq
    open System.Web
    open System.Globalization


    /// Object from Json 
    let unjson<'T> (jsonString:string)  : 'T =  
            use ms = new MemoryStream(System.Text.ASCIIEncoding.Default.GetBytes(jsonString)) 
            let obj = (new DataContractJsonSerializer(typeof<'T>)).ReadObject(ms) 
            obj :?> 'T


    /// Attempt to parse a tweet
    let parseTweet (tweet: string) = 
        try 
           let t = unjson<Tweet> tweet 
           match box t.User with 
           | null -> None
           | _ -> Some t
        with _ -> 
           None

module ConversationFunctions =
    // function that takes a map of users and thier tweets and returns a list set of "conversatations"
    // conversation is a set of users with a list of all their tweets
    let calculateConversations usersMap = // TODO use case insenative compares when comparing usernames
        let getSetOfResponses tweet =
            // get the people I mentioned in the tweet, but exculding me
            let mentionsExculdingMe = tweet.Entities.Mentions |> Seq.filter (fun x -> x.UserName <>. tweet.User.UserName)
            // get all the tweets that the people I have mentioned have made
            let possRespones =  mentionsExculdingMe |> Seq.choose(fun x -> Map.tryFind x.UserName usersMap) |> Seq.concat |> Seq.toList
            // find any tweets they made that mentioned me
            let actualRespones = possRespones  |> List.filter (fun response -> Array.exists (fun (mention: Mention) -> mention.UserName =. tweet.User.UserName) response.Entities.Mentions) 
            // if the response are none empty return the set of users
            match actualRespones with | [] -> None | x -> Some (x |> List.map(fun x -> x.User.UserName) |> Set.ofList)

        // get all the responses from the users list of conversations
        let getResponseToAnyTweet usersTweets =
            usersTweets 
            |> List.choose getSetOfResponses
            |> List.fold Set.union Set.empty
        // get all nonempty responses from all users 
        let nonEmpyResponse = 
            usersMap 
            |> Map.toList 
            |> List.map (fun (userName, tweets) -> userName, getResponseToAnyTweet tweets)
            |> List.filter (fun (_, responders) -> not (Set.isEmpty responders))
        // find the groups of people talking to each other
        let accumulateConversations acc (userName, responders) =
            // add the user name to the set of people that have responded to them
            let workingSet = Set.add userName responders
            // try and find any other set of people already in the accumilator that the user is talking to
            match acc |> List.tryFind (fun x -> not (Set.isEmpty  (Set.intersect x workingSet))) with
            // found no other overlaping conversations groups, create a new entry in the set
            | None -> workingSet :: acc
            // found set with some interactions
            | Some foundSet ->
                // take the set found out of the accumulator
                let accMinusFoundSet = acc |> List.filter (fun x -> foundSet <> x) 
                // add the set back into the accumulator updated with the new members
                let newAcc =  (Set.union foundSet workingSet) :: accMinusFoundSet
                if debug then printfn "### accMinusFoundSet = %A newAcc =%A" accMinusFoundSet newAcc
                newAcc

        // find all the tweets associated with the people in a conversation group
        let getAllTweetsInConversation particpants = 
            particpants,
            usersMap
            |> Map.filter (fun userName _ -> Set.contains userName particpants)
            |> Map.toList
            |> List.fold (fun acc (_,tweets) -> tweets @ acc) []
            |> List.sortBy (fun x -> x.StatusDate)

        // get the final data structure a map of sets of users with a list of all there tweets 
        let converastions =  nonEmpyResponse |> Seq.fold accumulateConversations []
        printfn "# converastions = %i, nonEmpyResponse = %i" (List.length converastions) (List.length nonEmpyResponse)
        converastions |> List.map getAllTweetsInConversation

    // add a new tweet by a user to the map of all tweets
    let addToTweetsByUser key x tweetsByUser =
        let prev = match Map.tryFind key tweetsByUser with None -> [] | Some v -> v
        Map.add x.User.UserName (x::prev) tweetsByUser

    // add a user to the mention counts map
    let addToMentionsCount key x mentionsCount =
        x.Entities.Mentions |> Seq.fold (fun acc userName -> 
            let count = match Map.tryFind userName mentionsCount with None -> 0 | Some v -> v
            Map.add userName (count+1) acc) mentionsCount  

/// A component which listens to tweets in the background and raises an
/// event each time a tweet is observed
type TwitterStreamFollower(userName:string, statUpdateCount: int, oauth_token, oauth_token_secret) =

    // gets a set of friends associated with the current userName
    let getMyFriendsSet() =
        // this uses methods that don't require authentication
        let wc = new WebClient()
        // get xml doc with user details to extract the user id
        let userDetailsDoc = XDocument.Parse (wc.DownloadString(sprintf "https://twitter.com/users/show/%s.xml" userName))
        let userId = userDetailsDoc.Root.Element(xn "id")
        // get a doc listing all the users friends
        let friendsIdsDoc = XDocument.Parse (wc.DownloadString(sprintf "https://api.twitter.com/1/friends/ids.xml?id=%s" userId.Value))
        // create a set of the current users id and all there friends id
        seq { yield  userId.Value; yield! friendsIdsDoc.Root.Descendants(xn "id") |> Seq.map (fun x -> x.Value) }
        |> Set.ofSeq

    // get the set of friend ids
    let friendSet = getMyFriendsSet()
 
    // the event that will be raised each time a tweat is received
    let tweetEvent = new Event<_>()
    
    // the url of the streaming api
    let streamFilterUrl = "https://stream.twitter.com/1/statuses/filter.json"
 
    // The cancellation condition
    let mutable group = new CancellationTokenSource()


    // An event which triggers on every 'n' triggers of the input event
    let every n (ev:IEvent<_>) =
       let out = new Event<_>()
       let count = ref 0
       ev.Add (fun arg -> incr count; if !count % n = 0 then out.Trigger arg)
       out.Publish
 
    /// Start listening to a stream of tweets
    member this.StartListening() =
        // Capture the synchronization context to allow us to raise events back on the GUI thread
        let syncContext = SynchronizationContext.CaptureCurrent()
 
        let listener =
                
            async { // create a web request to open a http stream following all our friends
                    let req = WebRequest.Create(streamFilterUrl,  Method = "POST", ContentType = "application/x-www-form-urlencoded")
                    let myFriendsList = System.String.Join(",", friendSet |> Seq.toArray)
                    if debug then printfn "myFriendsList = %s" myFriendsList  
                    req.AddOAuthHeader(oauth_token, oauth_token_secret, ["delimited", "length"; "follow", myFriendsList])
                    let doWriteBody() =
                        use reqStream = req.GetRequestStream() 
                        use streamWriter = new StreamWriter(reqStream)
                        streamWriter.Write(sprintf "delimited=length&follow=%s" myFriendsList)
                    doWriteBody()
                    
                    // use the request to open a response stream
                    use! resp = 
                        try
                            req.AsyncGetResponse()
                        with 
                        | :? WebException as ex ->
                            let x = ex.Response :?> HttpWebResponse
                            if x.StatusCode = HttpStatusCode.Unauthorized then
                                if debug then printfn "Connection to the stream failed: %O" ex
                            reraise()
                    use stream = resp.GetResponseStream()

                    // wrap stream in stream reader
                    use reader = new StreamReader(stream)
                    let rec loop() =
                        async {
                            let atEnd = reader.EndOfStream
                            if not atEnd then
                                // async loop to read tweets and raise an event when we receive one
                                let sizeLine = reader.ReadLine()
                                if debug then printfn "## [%A] read line: %s" DateTime.Now sizeLine
                                if String.IsNullOrEmpty sizeLine then return! loop()
                                let size = int sizeLine
                                let buffer = Array.zeroCreate size
                                let numRead = reader.ReadBlock(buffer,0,size) 
                                if debug then printfn "## [%A] finished reading blockl: %i" DateTime.Now numRead
                                let text = new System.String(buffer)
                                if tweetEvent :> obj = null then failwith "tweet event null"
                                syncContext.RaiseEvent tweetEvent text
                                return! loop()
                        }
                    return! loop()  }
 
        Async.Start(listener, group.Token)
 
    /// Stop listening to a stream of tweets
    member this.StopListening() =
        group.Cancel();
        group <- new CancellationTokenSource()
 
    /// Raised when the json for a tweet arrives
    member this.SupersetNewRawTweet = tweetEvent.Publish

    /// raised when we can sucessfully parse a received tweet 
    member this.SupersetNewParsedTweet = tweetEvent.Publish |> Event.choose JsonParsering.parseTweet

    /// raised when we receive a tweet that we would normally see
    member this.NewParsedTweet = 
        this.SupersetNewParsedTweet 
        |> Event.filter (fun x -> 
                            let isGoodTweet = Set.contains x.User.Id friendSet || Array.exists (fun (ment: Mention)  -> ment.UserName =. userName) x.Entities.Mentions 
                            if debug then printfn "%b %s" isGoodTweet x.User.Id
                            isGoodTweet)

    /// raised with an update of the master map that holds the users and their tweets
    member this.TweetsByUserUpdate = 
        this.SupersetNewParsedTweet
        |> Event.scan (fun map tweet -> ConversationFunctions.addToTweetsByUser tweet.User.UserName tweet map) Map.empty
        |> every statUpdateCount

    /// raised when a new update of the counts
    member this.MentionsCountsUpdate = 
        this.SupersetNewParsedTweet
        |> Event.scan (fun map tweet -> ConversationFunctions.addToMentionsCount tweet.User.UserName tweet map) Map.empty
        |> every statUpdateCount

    /// raised an event with the conversation data 
    member this.ConversationsUpdate =
        this.TweetsByUserUpdate
        |> Event.map ConversationFunctions.calculateConversations