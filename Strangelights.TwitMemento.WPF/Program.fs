open System
open System.Collections.Generic
open System.Net.Cache
open System.Windows
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Windows.Controls
open System.Windows.Data
open System.Collections.ObjectModel
open Strangelights.TwitMemento

// TODO handle login failures
// TODO protect screte ??
// TODO meneto like look and feel
// TOOD make all tweets window roughtly the same as twitter time line
// TODO add some interesting stats
// TODO more comments?
// TODO convert to grid with splitterss??
// TODO fix closing problem.
// TOOD auto scroll conversation containers.
// TODO general tidy up and remove all the bits that make me want to cry

// may need this if ever anything goes very wrong with authentication
//Storage.reset()

let createOAuthWindow() =
    let oauthText = new Label(Content = "Twit Memento use's OAuth. We've launched a browser so you can log on to twitter and get a pin. Any problems, push the reset button.")
    let usernameLab = new Label(Content = "Username", Width= 200.)
    let username = new TextBox(Width= 200.)
    let usernameStack = new StackPanel(Orientation = Orientation.Horizontal)
    usernameStack.Children.Add(usernameLab) |> ignore
    usernameStack.Children.Add(username) |> ignore
    let passwordLab = new Label(Content = "Pin (NOT your passwors)", Width= 200.)
    let password = new TextBox(Width= 200.)
    let passwordStack = new StackPanel(Orientation = Orientation.Horizontal)
    passwordStack.Children.Add(passwordLab) |> ignore
    passwordStack.Children.Add(password) |> ignore
    let okay = new Button(Content = "Okay")
    let reset = new Button(Content = "Reset")
    let stackPanel = new StackPanel()
    stackPanel.Children.Add(oauthText) |> ignore
    stackPanel.Children.Add(usernameStack) |> ignore
    stackPanel.Children.Add(passwordStack) |> ignore
    stackPanel.Children.Add(okay) |> ignore
    stackPanel.Children.Add(reset) |> ignore
    let window = new Window(Content = stackPanel, Width= 200., Height = 200.)
    okay.Click.Add(fun _ -> window.Close())
    reset.Click.Add(fun _ -> Storage.reset())
    let oauth_token, oauth_token_secret, usernameText = StoredOAuth.getOAuth (fun _ -> window.ShowDialog() |> ignore; password.Text, username.Text)
    oauth_token, oauth_token_secret, usernameText

let allTweetsOC = new ObservableCollection<UserStatus>()
let conversationsDict = new Dictionary<Set<string>, ObservableCollection<UserStatus>*ScrollViewer>()


type ImageConvert()=
    interface IValueConverter with
        member x.Convert(value, targetType, parameter, culture) =
            new BitmapImage(new Uri((string)value), new RequestCachePolicy(RequestCacheLevel.CacheIfAvailable)) :> obj
        member x.ConvertBack(value, targetType, parameter, culture) =
            raise (new NotImplementedException())

let createScrollingViewer tweets =
    let readOnlyTextBoxFactory (partenFactory: FrameworkElementFactory) bindTo =
        let textBoxFactory = new FrameworkElementFactory(typeof<TextBox>)
        textBoxFactory.SetValue(Control.BorderThicknessProperty, new Thickness(0.))
        textBoxFactory.SetValue(TextBox.IsReadOnlyProperty, true)
        textBoxFactory.SetBinding(TextBox.TextProperty, new Binding(bindTo, Mode = BindingMode.OneWay))
        partenFactory.AppendChild(textBoxFactory)
        textBoxFactory

    let createTweetContainerTemplate() =
        let dpFactory = new FrameworkElementFactory(typeof<DockPanel>, Name = "tweetContainer")
        dpFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal)
        let imageFactory = new FrameworkElementFactory(typeof<Image>)
        let converter = new ImageConvert() :> IValueConverter
        imageFactory.SetBinding(Image.SourceProperty, new Binding("ProfileImage", Mode = BindingMode.OneWay, Converter = converter))
        imageFactory.SetValue(TextBox.WidthProperty, 48.)
        imageFactory.SetValue(TextBox.HeightProperty, 48.)
        dpFactory.AppendChild(imageFactory)

        let spvFactory = new FrameworkElementFactory(typeof<StackPanel>, Name = "tweetTextContainer")
        dpFactory.AppendChild(spvFactory)

        let username = readOnlyTextBoxFactory spvFactory "UserName"
        username.SetValue(TextBox.FontWeightProperty, FontWeights.Bold)
        let status = readOnlyTextBoxFactory spvFactory "Status"
        status.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap)
        let statusDate = readOnlyTextBoxFactory spvFactory "StatusDate"
        statusDate.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Right)
        statusDate.SetValue(TextBox.FontStyleProperty, FontStyles.Italic)
        dpFactory

    let tweetTemplate = new DataTemplate(DataType = typeof<UserStatus>, VisualTree = createTweetContainerTemplate())

    new ScrollViewer(Content = new ItemsControl(ItemsSource = tweets, Width = 300., ItemTemplate = tweetTemplate))

let conversationsDockPanel = new DockPanel(LastChildFill = false)
let conversationsScrollViewer = new ScrollViewer(Content = conversationsDockPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, 
                                                 HorizontalScrollBarVisibility = ScrollBarVisibility.Visible)

//conversationsScrollViewer.
let treatConversations cons =
    // handle creating conversation windows and merging tweets into the OCs
    for (partipants, tweets) in cons do
        if not (conversationsDict.ContainsKey partipants) then
            let converOC = new ObservableCollection<UserStatus>()
            let scrollViewer = createScrollingViewer converOC
            conversationsDict.Add(partipants, (converOC, scrollViewer))
            conversationsDockPanel.Children.Add(scrollViewer) |> ignore
        let converOC,_ = conversationsDict.[partipants]
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
            let _, scrollViewer = conversationsDict.[partipants]
            deleteList.Add(partipants)
            conversationsDockPanel.Children.Remove(scrollViewer)
    for partipants in deleteList do
        conversationsDict.Remove(partipants) |> ignore


let allTweets = createScrollingViewer allTweetsOC

let createTweetBox() =
    let textBox = new TextBox(AcceptsReturn = true, Height = 50.)
    let counter = new TextBlock(Text="140", Width=100., HorizontalAlignment = HorizontalAlignment.Center, 
                                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center)
    let button = new Button(Content="Tweet!", Width=160.)
    let dockPanel = new DockPanel()

    textBox.KeyDown.Add(fun _ -> counter.Text  <-  sprintf "%i" (140 - textBox.Text.Length))
    button.Click.Add(fun _ -> OtherTwitterStuff.updateStatus textBox.Text |> ignore; counter.Text <- "140"; textBox.Text <- "")
    counter.SetValue(DockPanel.DockProperty, Dock.Right)
    button.SetValue(DockPanel.DockProperty, Dock.Right)
    List.map dockPanel.Children.Add [button :> UIElement; counter :> UIElement; textBox :> UIElement;] |> ignore
    dockPanel.SetValue(DockPanel.DockProperty, Dock.Top)
    dockPanel

let outerMainContainer = new DockPanel()
outerMainContainer.Children.Add(createTweetBox()) |> ignore
let mainContainer = new DockPanel()
mainContainer.Children.Add(allTweets)|> ignore
mainContainer.Children.Add(conversationsScrollViewer)|> ignore
outerMainContainer.Children.Add(mainContainer) |> ignore

let app = new Application()

let window = new Window(Content = outerMainContainer, Title = "Twit Memento an F# Twitter client by Robert Pickering")
window.Loaded.Add(fun _ ->
    System.Net.ServicePointManager.Expect100Continue <- false
    let oauth_token, oauth_token_secret, username = createOAuthWindow()
    let incoming = new TwitterStreamFollower(username, 5, oauth_token, oauth_token_secret)
    incoming.NewParsedTweet.Add(fun x -> allTweetsOC.Add(x); allTweets.ScrollToBottom())
    incoming.ConversationsUpdate.Add treatConversations
    window.Closing.Add(fun _ -> incoming.StopListening())
    incoming.StartListening())


[<System.STAThread>]
app.Run(window) |> ignore


//rather unsucessful experiment with grid splitters ... seems complicated
//let mainContainer = new Grid()
//let gridSplitter = new GridSplitter(Width=10.,Background = Brushes.LightSlateGray)
//mainContainer.ColumnDefinitions.Add(new ColumnDefinition())
//mainContainer.ColumnDefinitions.Add(new ColumnDefinition())
//mainContainer.ColumnDefinitions.Add(new ColumnDefinition())
//Grid.SetColumn(allTweets, 0)
//Grid.SetRow(allTweets, 0)
//Grid.SetColumn(gridSplitter, 1)
//Grid.SetRow(gridSplitter, 0)
//Grid.SetColumn(conversationsScrollViewer, 2)
//Grid.SetRow(conversationsScrollViewer, 0)
//mainContainer.Children.Add(allTweets) |> ignore
//mainContainer.Children.Add(gridSplitter) |> ignore
//mainContainer.Children.Add(conversationsScrollViewer) |> ignore
//outerMainContainer.Children.Add(mainContainer) |> ignore
