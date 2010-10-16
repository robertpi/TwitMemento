module Strangelights.TwitMemento.Storage
open System
open System.IO
open System.IO.IsolatedStorage

let isoStore =  IsolatedStorageFile.GetStore(IsolatedStorageScope.User ||| IsolatedStorageScope.Assembly, null, null)

let filename = "Keys.txt"

let getFileStreamWriter mode = new StreamWriter(new IsolatedStorageFileStream(filename, mode, isoStore))
let getFileStreamReader() = new StreamReader(new IsolatedStorageFileStream(filename, FileMode.Open, isoStore))

let reset() = isoStore.DeleteFile(filename)

let storeKeys oauth_token oauth_token_secret =
    use stream = getFileStreamWriter FileMode.Create
    stream.WriteLine(oauth_token: string)
    stream.WriteLine(oauth_token_secret: string)

let storePinAndUsername pin username =
    use stream = getFileStreamWriter FileMode.Append
    stream.WriteLine(pin: string)
    stream.WriteLine(username: string)

let readKeys() =
    let files = isoStore.GetFileNames(filename)
    if files.Length > 0 then
        use stream = getFileStreamReader()
        let lines =
            stream.ReadToEnd().Split([|System.Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries) |> List.ofSeq
        match lines with
        | [] -> None, None, None, None
        | [ oauth_token; oauth_token_secret ] -> Some oauth_token, Some oauth_token_secret, None, None
        | [ oauth_token; oauth_token_secret; pin; username ] -> Some oauth_token, Some oauth_token_secret, Some pin, Some username
        | _ -> failwith "unknown number of items"
    else
        None, None, None, None