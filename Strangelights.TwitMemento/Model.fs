namespace Strangelights.TwitMemento
open System.Runtime.Serialization
open System.Runtime.Serialization.Json

[<DataContract>]
type Mention =
    { [<field: DataMember(Name="id_str") >]
      mutable Id: string 
      [<field: DataMember(Name="screen_name") >]
      mutable UserName: string }

[<DataContract>]
type Entities =
    { [<field: DataMember(Name="user_mentions") >]
      mutable Mentions: Mention[] }

/// The results of the parsed tweet
[<DataContract>]
type UserStatus = 
    { [<field: DataMember(Name="id_str") >]
      mutable Id : string;
      [<field: DataMember(Name="screen_name") >]
      mutable UserName : string;
      [<field: DataMember(Name="profile_image_url") >]
      mutable ProfileImage : string;
      [<field: DataMember(Name="friends_count") >]
      mutable FriendsCount : int;
      [<field: DataMember(Name="followers_count") >]
      mutable FollowersCount : int;
      [<field: DataMember(Name="created_at") >]
      mutable JoinDate : string
    }

[<DataContract>]
type Tweet = 
    { [<field: DataMember(Name="id") >]
      mutable Id : string;
      [<field: DataMember(Name="user") >]
      mutable User : UserStatus 
      [<field: DataMember(Name="created_at") >]
      mutable StatusDate : string
      [<field: DataMember(Name="entities") >]
      mutable Entities: Entities
      [<field: DataMember(Name="text") >]
      mutable Status: string;  }


