#r "System.Runtime.Serialization"
#r "System.Xml.Linq"
#load "OAuth.fs"
#load "Storage.fs"
#load "Model.fs"
#load "TwitterStreamFollower.fs"
open Strangelights.TwitMemento

// some hand rolled test data
let  mention: Mention =
    { Id = "30888410";
      UserName = "ptrelford " }

let entities =
    { Mentions= [| mention |] }

let userStatus = 
    { Id = "14581910";
      UserName = "robertpi";
      ProfileImage = "http://a0.twimg.com/profile_images/429301333/me_bains_des_paquisi_med_normal.jpg";
      FriendsCount = 793;
      FollowersCount = 704;
      JoinDate = "Tue Apr 29 07:35:09 +0000 2008"}

let tweet = 
    { Id = "77777777777777";
      User = userStatus
      StatusDate = "Fri Nov 4 7:35:09 +0000 2011"
      Entities= entities
      Status = "Bought some coffee. Waiting for @ptrelford";  }

let rnd = new System.Random()

// useful function for creating test data
let createTweet username userId status mentions =
    let mentions = 
        Seq.map (fun (id, username) -> { Id = id; UserName = username }: Mention) mentions
        |> Array.ofSeq

    let entities =
        { Mentions = mentions }

    let userStatus = 
        { Id = "";
          UserName = "";
          ProfileImage = "";
          FriendsCount = 0;
          FollowersCount = 0;
          JoinDate = ""}

    let tweet = 
        { Id = string (rnd.Next());
          User = userStatus
          StatusDate = ""
          Entities= entities
          Status = status;  }

    tweet

let userTweetMap = 
    ["robertpi", [ tweet ]]
    |> Map.ofList

ConversationFunctions.calculateConversations userTweetMap

