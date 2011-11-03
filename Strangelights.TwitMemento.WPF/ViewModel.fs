module ViewModel
open System.Collections.Generic
open System.Collections.ObjectModel
open System.Windows.Controls
open Strangelights.TwitMemento

// dictionary that keeps track of which conversations are grouped together
let private conversationsDict = new Dictionary<Set<string>, ObservableCollection<Tweet>>()

// the properties of the view model that we'll bind to
let AllTweetsOC = new ObservableCollection<Tweet>()
let ConversationsOC = new ObservableCollection<ObservableCollection<Tweet>>()

// take the conversations list and add it to the view modol
let treatConversations cons =
    // handle creating conversation windows and merging tweets into the OCs
    for (partipants, tweets) in cons do
        if not (conversationsDict.ContainsKey partipants) then
            let converOC = new ObservableCollection<Tweet>()
            ConversationsOC.Add converOC
            conversationsDict.Add(partipants, converOC)
        let converOC = conversationsDict.[partipants]
        // this list merge algo is ugly, feels like we could do better
        for tweet in tweets do
            if not (converOC.Contains tweet) then
                if converOC.Count > 0 && converOC.[0].StatusDate > tweet.StatusDate then
                    converOC.Insert(0, tweet)
                for index = 0 to converOC.Count - 2 do
                    if converOC.[index].StatusDate < tweet.StatusDate && tweet.StatusDate < converOC.[index + 1].StatusDate then
                        converOC.Insert(index, tweet)
            if not (converOC.Contains tweet) then
                converOC.Add(tweet)

    // handle removing conversations that have been merged
    let deleteList = new ResizeArray<_>()
    for partipants in conversationsDict.Keys do
        if not (List.exists (fun (x, _) -> x = partipants) cons) then
            let conv = conversationsDict.[partipants]
            deleteList.Add(partipants)
            ConversationsOC.Remove(conv) |> ignore
    for partipants in deleteList do
        conversationsDict.Remove(partipants) |> ignore
