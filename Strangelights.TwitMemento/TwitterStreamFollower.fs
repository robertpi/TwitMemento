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

// naughty!
open Misc

/// The results of the parsed tweet
type UserStatus =
    { Id: string
      UserName : string
      ProfileImage : string
      Status : string
      Mentions : list<string>
      StatusDate : DateTime }


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
        // TODO assume we've been authenticated at this point, kinda shoddy
        let oauth_token, oauth_token_secret =
            match Storage.readKeys() with
            | Some oauth_token, Some oauth_token_secret, Some _, Some _ ->
                oauth_token, oauth_token_secret
            | _ -> failwith "asset false"
        let statusUrl = "http://twitter.com/statuses/update.xml"
        let request = WebRequest.Create (statusUrl, Method="POST")
        let tweet =  OAuth.urlEncode(status)
        request.AddOAuthHeader(oauth_token,oauth_token_secret,["status",tweet])
        use bodyStream = request.GetRequestStream()
        use bodyWriter = new StreamWriter(bodyStream)
        bodyWriter.Write("status=" + tweet)
        bodyWriter.Close()
        use resp = request.GetResponse()
        use strm = resp.GetResponseStream()
        let text = (new StreamReader(strm)).ReadToEnd()
        text


/// A component which listens to tweets in the background and raises an
/// event each time a tweet is observed
type TwitterStreamFollower(userName:string, statUpdateCount: int, oauth_token, oauth_token_secret) =

    let getMyFriendsList() =
        let wc = new WebClient()
        // add oauth header
        // TODO limit to 500 user ids or whatever the api limits us to ...
        let userDetailsDoc = XDocument.Parse (wc.DownloadString(sprintf "http://twitter.com/users/show/%s.xml" userName))
        let userId = userDetailsDoc.Root.Element(xn "id")
        let friendsIdsDoc = XDocument.Parse (wc.DownloadString(sprintf "http://twitter.com/friends/ids.xml?user_id=%s" userId.Value))
        seq { yield  userId.Value; yield! friendsIdsDoc.Root.Elements(xn "id") |> Seq.map (fun x -> x.Value) }
        |> Set.ofSeq

    let friendSet = getMyFriendsList()
 
 
    let tweetEvent = new Event<_>()  
    let streamSampleUrl = "http://stream.twitter.com/1/statuses/sample.xml?delimited=length"
    let streamFilterUrl = "http://stream.twitter.com/1/statuses/filter.xml"
 
    /// The cancellation condition
    let mutable group = new CancellationTokenSource()

    /// Attempt to parse a tweet
    let parseTweet (xml: string) =  
        let document = XDocument.Parse xml
        let node = document.Root
        if node.Element(xn "user") <> null then
            let status = node.Element(xn "text").Value |> HttpUtility.HtmlDecode
            Some { Id           = node.Element(xn "user").Element(xn "id").Value
                   UserName     = node.Element(xn "user").Element(xn "screen_name").Value;
                   ProfileImage = node.Element(xn "user").Element(xn "profile_image_url").Value;
                   Status       = status;
                   Mentions     = [ for userNameCapture in twitterUserName.Matches(status) do yield userNameCapture.Value.[ 1 .. ] ]
                   StatusDate   = node.Element(xn "created_at").Value |> (fun msg ->
                                        DateTime.ParseExact(msg, "ddd MMM dd HH:mm:ss +0000 yyyy",
                                                            CultureInfo.InvariantCulture)); }
        else
            None


    let calculateSummerizeMentions mentions =
        let topMentions = mentions |> Map.toSeq |> Seq.fold (fun ((_, max) as acc) (user, count) -> if count > max then  user, count else acc) ("", 0)
        let moreThanOnMention = mentions |> Map.toSeq |> Seq.filter (fun (_, count) -> count > 1) |> Seq.length
        topMentions, moreThanOnMention

    let (=.) s1 s2 =
        String.Compare(s1, s2, StringComparison.InvariantCultureIgnoreCase) = 0

    let (<>.) s1 s2 = not  (s1 =. s2) 

    let calculateConversations usersMap = // TODO use case insenative compares when comparing usernames
        let getSetOfResponses tweet =
            let mentionsExculdingMe = tweet.Mentions |> List.filter (fun x -> x <>. tweet.UserName)
            let possRespones =  mentionsExculdingMe |> List.choose(fun x -> Map.tryFind x usersMap) |> List.collect id
            let actualRespones = possRespones  |> List.filter (fun response -> List.exists (fun mention -> mention =. tweet.UserName) response.Mentions) 
            // 
            //printfn "##mentions = %d, possible responses = %d, actual responses = %d" (List.length mentionsExculdingMe) (List.length possRespones) (List.length actualRespones)
            match actualRespones with | [] -> None | x -> Some (x |> List.map(fun x -> x.UserName) |> Set.ofList )
        let getResponseToAnyTweet usersTweets =
            usersTweets 
            |> List.choose getSetOfResponses
            |> List.fold Set.union Set.empty
        let nonEmpyResponse = 
            usersMap |> Map.toList |> List.map (fun (userName, tweets) -> userName, getResponseToAnyTweet tweets)
            |> List.filter (fun (_, responders) -> not (Set.isEmpty responders))
        let acculateConversations acc (userName, responders) =
            let workingSet = Set.add userName responders
            match acc |> List.tryFind (fun x -> not (Set.isEmpty  (Set.intersect x workingSet))) with
            | None -> workingSet :: acc
            | Some foundSet ->
                let accMinusFoundSet = acc |> List.filter (fun x -> foundSet <> x) 
                let newAcc =  (Set.union foundSet workingSet) :: accMinusFoundSet
                printfn "### accMinusFoundSet = %A newAcc =%A" accMinusFoundSet newAcc
                newAcc
        // TODO reorganise this debug stuff
        //let avg = usersMap |> Seq.averageBy (fun (KeyValue(_,d)) -> float d.Length)
        //printfn "#users = %d, avg tweets = %g, one+ mentions = %d, non empty responses = %d, conversations = %i" usersMap.Count avg moreThanOnMention (Seq.length nonEmpyResponse) (List.length converastions)
        let getAllTweetsInConversation particpants = 
            //printfn "*** Particpants: %A"  particpants
            particpants,
            usersMap
            |> Map.filter (fun userName _ -> Set.contains userName particpants)
            |> Map.toList
            |> List.fold (fun acc (_,tweets) -> tweets @ acc) []
            |> List.sortBy (fun x -> x.StatusDate)

        let converastions =  nonEmpyResponse |> Seq.fold acculateConversations []
        printfn "# converastions = %i, converastions = %i" (List.length converastions) (List.length nonEmpyResponse)
        converastions |> List.map getAllTweetsInConversation

    let addToTweetsByUser key x tweetsByUser =
        let prev = match Map.tryFind key tweetsByUser with None -> [] | Some v -> v
        Map.add x.UserName (x::prev) tweetsByUser

    let addToMentionsCount key x mentionsCount =
        x.Mentions |> Seq.fold (fun acc userName -> 
            let count = match Map.tryFind userName mentionsCount with None -> 0 | Some v -> v
            Map.add userName (count+1) acc) mentionsCount  

    /// An event which triggers on every 'n' triggers of the input event
    let every n (ev:IEvent<_>) =
       let out = new Event<_>()
       let count = ref 0
       ev.Add (fun arg -> incr count; if !count % n = 0 then out.Trigger arg)
       out.Publish
 
    /// Start listening to a stream of tweets
    member this.StartListening() =
                                                                    /// The background process

        // Capture the synchronization context to allow us to raise events back on the GUI thread
        let syncContext = SynchronizationContext.CaptureCurrent()
 
        let listener =
                
            async { let req = WebRequest.Create(streamFilterUrl,  Method = "POST", ContentType = "application/x-www-form-urlencoded")
                    let myFriendsList = System.String.Join(",", friendSet |> Seq.toArray)
                    if debug then printfn "myFriendsList = %s" myFriendsList  
                    req.AddOAuthHeader(oauth_token, oauth_token_secret, ["delimited", "length"; "follow", myFriendsList])
                    let doWriteBody() =
                        use reqStream = req.GetRequestStream() 
                        use streamWriter = new StreamWriter(reqStream)
                        streamWriter.Write(sprintf "delimited=length&follow=%s" myFriendsList)
                    doWriteBody()
                        
                    use! resp = 
                        try
                            req.AsyncGetResponse()
                        with 
                        | :? WebException as ex ->
                            let x = ex.Response :?> HttpWebResponse
                            if x.StatusCode = HttpStatusCode.Unauthorized then
                                // TODO need inform user login has failed and they need to try again
                                printfn "Here?? %O" ex
                            reraise()
                    use stream = resp.GetResponseStream()
                    use reader = new StreamReader(stream)
                    let atEnd = reader.EndOfStream
                    let rec loop() =
                        async {
                            let atEnd = reader.EndOfStream
                            if not atEnd then
                                let x = _arg8
                                let sizeLine = reader.ReadLine()
                                if debug then printfn "## [%A] read line: %s" DateTime.Now sizeLine
                                if String.IsNullOrEmpty sizeLine then return! loop()
                                let size = int sizeLine
                                let buffer = Array.zeroCreate size
                                let _numRead = reader.ReadBlock(buffer,0,size) 
                                if debug then printfn "## [%A] finished reading blockl: %i" DateTime.Now _numRead
                                let text = new System.String(buffer)
                                syncContext.RaiseEvent tweetEvent text
                                return! loop()
                        }
                    return! loop()  }
 
        Async.Start(listener, group.Token)
 
    /// Stop listening to a stream of tweets
    member this.StopListening() =
        group.Cancel();
        group <- new CancellationTokenSource()
 
    /// Raised when the XML for a tweet arrives
    member this.SupersetNewRawTweet = tweetEvent.Publish

    member this.SupersetNewParsedTweet = tweetEvent.Publish |> Event.choose parseTweet

    member this.NewParsedTweet = 
        this.SupersetNewParsedTweet 
        |> Event.filter (fun x -> Set.contains x.Id friendSet || List.exists (fun ment -> ment =. userName) x.Mentions )

    member this.TweetsByUserUpdate = 
        this.SupersetNewParsedTweet
        |> Event.scan (fun map tweet -> addToTweetsByUser tweet.UserName tweet map) Map.empty
        |> every statUpdateCount

    member this.MentionsCountsUpdate = 
        this.SupersetNewParsedTweet
        |> Event.scan (fun map tweet -> addToMentionsCount tweet.UserName tweet map) Map.empty
        |> every statUpdateCount

    member this.ConversationsUpdate =
        this.TweetsByUserUpdate
        |> Event.map calculateConversations

