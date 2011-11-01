[<AutoOpen>]
module WpfHelpers
open System.Windows

type System.Windows.Controls.Panel with
    member x.AddChildren (values : list<UIElement>) =
        for value in values do x.Children.Add value |> ignore


