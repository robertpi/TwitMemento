module View
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

// creates a window to collect the oauth information
let createOAuthWindow() =
    let oauthText = new Label(Content = "Twit Memento use's OAuth. We've launched a browser so you can log on to twitter and get a pin. Any problems, push the reset button.")
    let usernameLab = new Label(Content = "Username", Width= 200.)
    let username = new TextBox(Width= 200.)
    let usernameStack = new StackPanel(Orientation = Orientation.Horizontal)
    usernameStack.AddChildren([usernameLab; username])

    let passwordLab = new Label(Content = "Pin (NOT your passwors)", Width= 200.)
    let password = new TextBox(Width= 200.)
    let passwordStack = new StackPanel(Orientation = Orientation.Horizontal)
    passwordStack.AddChildren([passwordLab; password])

    let okay = new Button(Content = "Okay")
    let reset = new Button(Content = "Reset")
    let stackPanel = new StackPanel()
    stackPanel.AddChildren([oauthText; usernameStack; passwordStack; okay; reset])

    let window = new Window(Content = stackPanel, Width= 400., Height = 400.)
    okay.Click.Add(fun _ -> window.Close())
    reset.Click.Add(fun _ -> Storage.reset())

    let oauth_token, oauth_token_secret, usernameText = StoredOAuth.getOAuth (fun _ -> window.ShowDialog() |> ignore; password.Text, username.Text)
    oauth_token, oauth_token_secret, usernameText

// convert to convert between urls and actual urls of thoses images
type ImageConvert()=
    interface IValueConverter with
        member x.Convert(value, targetType, parameter, culture) =
            new BitmapImage(new Uri((string)value), new RequestCachePolicy(RequestCacheLevel.CacheIfAvailable),
                            CreateOptions = BitmapCreateOptions.IgnoreColorProfile) :> obj
        member x.ConvertBack(value, targetType, parameter, culture) =
            raise (new NotImplementedException())

// element factories to support our data bind
module private ElementFactories =
    // create a readonly text box bound to the given property name
    let readOnlyTextBoxFactory (partenFactory: FrameworkElementFactory) bindTo =
        let textBoxFactory = new FrameworkElementFactory(typeof<TextBox>)
        textBoxFactory.SetValue(Control.BorderThicknessProperty, new Thickness(0.))
        textBoxFactory.SetValue(TextBox.IsReadOnlyProperty, true)
        textBoxFactory.SetBinding(TextBox.TextProperty, new Binding(bindTo, Mode = BindingMode.OneWay))
        partenFactory.AppendChild(textBoxFactory)
        textBoxFactory

    // create a container that will show a tweet
    let createTweetContainerTemplate() =
        let dpFactory = new FrameworkElementFactory(typeof<DockPanel>, Name = "tweetContainer")
        dpFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal)
        let imageFactory = new FrameworkElementFactory(typeof<Image>)
        let converter = new ImageConvert() :> IValueConverter
        imageFactory.SetBinding(Image.SourceProperty, new Binding("User.ProfileImage", Mode = BindingMode.OneWay, Converter = converter))
        imageFactory.SetValue(TextBox.WidthProperty, 48.)
        imageFactory.SetValue(TextBox.HeightProperty, 48.)
        dpFactory.AppendChild(imageFactory)

        let spvFactory = new FrameworkElementFactory(typeof<StackPanel>, Name = "tweetTextContainer")
        dpFactory.AppendChild(spvFactory)

        let username = readOnlyTextBoxFactory spvFactory "User.UserName"
        username.SetValue(TextBox.FontWeightProperty, FontWeights.Bold)
        let status = readOnlyTextBoxFactory spvFactory "Status"
        status.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap)
        let statusDate = readOnlyTextBoxFactory spvFactory "StatusDate"
        statusDate.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Right)
        statusDate.SetValue(TextBox.FontStyleProperty, FontStyles.Italic)
        dpFactory

    // create a scroll view that will show a list of tweets
    let createScrollingViewer tweets =
        let tweetTemplate = new DataTemplate(DataType = typeof<UserStatus>, VisualTree = createTweetContainerTemplate())

        new ScrollViewer(Content = new ItemsControl(ItemsSource = tweets, Width = 300., ItemTemplate = tweetTemplate))
    
    // create a double scroll view that will show our list of conversations
    let createDoubleScrollingViewer allConversationsTweets =
        let icFactory = new FrameworkElementFactory(typeof<ItemsControl>, Name = "conversationContainer")
        icFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(Mode = BindingMode.OneWay))
        let tweetTemplate = new DataTemplate(DataType = typeof<UserStatus>, VisualTree = createTweetContainerTemplate())
        icFactory.SetValue(ItemsControl.ItemTemplateProperty, tweetTemplate)
        let svFactory = new FrameworkElementFactory(typeof<ScrollViewer>, Name = "conversationContainerScrollViewer")
        svFactory.AppendChild(icFactory)
        svFactory.SetValue(Control.WidthProperty, 300.)
        let conversationTemplate = new DataTemplate(DataType = typeof<ObservableCollection<UserStatus>>, VisualTree = svFactory)
        let spFactory = new FrameworkElementFactory(typeof<StackPanel>, Name = "horizonalScrollView")
        spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal)
        new ScrollViewer(Content = new ItemsControl(ItemsSource = allConversationsTweets, 
                                                    ItemTemplate = conversationTemplate,
                                                    ItemsPanel = new ItemsPanelTemplate(spFactory)))

// the scroll view that will show our list of conversations bound to the view model conversation list
let private conversationsScrollViewer = ElementFactories.createDoubleScrollingViewer ViewModel.ConversationsOC

// scoll viewer that will show our list of tweets
let AllTweets = ElementFactories.createScrollingViewer ViewModel.AllTweetsOC

// box that will allow the user to tweet
let private createTweetBox() =
    let textBox = new TextBox(AcceptsReturn = true, Height = 50.)
    let counter = new TextBlock(Text="140", Width=100., HorizontalAlignment = HorizontalAlignment.Center, 
                                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center)
    let button = new Button(Content="Tweet!", Width=160.)
    let dockPanel = new DockPanel()

    textBox.KeyDown.Add(fun _ -> counter.Text  <-  sprintf "%i" (140 - textBox.Text.Length))
    button.Click.Add(fun _ -> OtherTwitterStuff.updateStatus textBox.Text |> ignore; counter.Text <- "140"; textBox.Text <- "")
    counter.SetValue(DockPanel.DockProperty, Dock.Right)
    button.SetValue(DockPanel.DockProperty, Dock.Right)
    dockPanel.AddChildren([button; counter; textBox;])
    dockPanel.SetValue(DockPanel.DockProperty, Dock.Top)
    dockPanel

// final wiring up of all the containers
let private outerMainContainer = new DockPanel()
let private mainContainer = new DockPanel()
mainContainer.AddChildren([AllTweets; conversationsScrollViewer])
outerMainContainer.AddChildren([createTweetBox(); mainContainer]) |> ignore

// the main window that shows our container
let MainWindow = new Window(Content = outerMainContainer, Title = "Twit Memento an F# Twitter client by Robert Pickering")
