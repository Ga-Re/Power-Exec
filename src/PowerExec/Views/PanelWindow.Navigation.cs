using PowerExec.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerExec.Core.Config;
using PowerExec.Core.Layout;
using PowerExec.Desktop.Infrastructure;
using PowerExec.Desktop.Interop;
using PowerExec.UI.Services;
using PowerExec.Desktop.Services;
using PowerExec.UI.ViewModels;

namespace PowerExec.Desktop.Views;

/// <content>Navigation in langen Listen: fixierter Gruppenkopf, Sprungleiste, Strg+K.</content>
public partial class PanelWindow
{
    // ---- Fixierter Gruppenkopf, Sprungleiste, Strg+K ---------------------------

    private bool _stickyUpdatePending;

    private void ScheduleStickyUpdate()
    {
        _groupContainers = null;
        if (_stickyUpdatePending) return;
        _stickyUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _stickyUpdatePending = false;
            UpdateStickyGroup();
        });
    }

    private List<(GroupViewModel Group, FrameworkElement Element)>? _groupContainers;

    private List<(GroupViewModel Group, FrameworkElement Element)> GroupContainers()
    {
        if (_groupContainers != null) return _groupContainers;
        var list = new List<(GroupViewModel Group, FrameworkElement Element)>();
        void Walk(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ContentPresenter { Content: GroupViewModel g } cp) list.Add((g, cp));
                Walk(child);
            }
        }
        Walk(ListScroll);
        return _groupContainers = list;
    }

    private void ListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateStickyGroup();

    /// <summary>Innerste Gruppe, deren Kopf oben herausgescrollt ist, deren Inhalt aber noch sichtbar ist.</summary>
    private void UpdateStickyGroup()
    {
        if (!IsLoaded || ListScroll.VerticalOffset < 1) { Vm.StickyGroup = null; return; }
        const double headerHeight = 34;
        GroupViewModel? best = null;
        double bestTop = double.MinValue;
        foreach (var (group, element) in GroupContainers())
        {
            if (!element.IsVisible) continue; // eingeklappte Untergruppe
            if (!element.IsDescendantOf(ListScroll)) { _groupContainers = null; continue; }
            var top = element.TranslatePoint(new Point(0, 0), ListScroll).Y;
            if (top < 0 && top + element.ActualHeight > headerHeight && top > bestTop && group.IsExpanded)
            {
                best = group;
                bestTop = top;
            }
        }
        Vm.StickyGroup = best;
        if (best != null) AlignStickyHeader(GroupContainers().First(x => ReferenceEquals(x.Group, best)).Element);
    }

    /// <summary>
    /// Fixierten Kopf genau über die Karte der Gruppe legen: gleiche linke und rechte Kante (Einrückung von Untergruppen,
    /// Scrollbalken, Sprungleiste) – so ragt er weder über die Karte hinaus noch schaut deren Ecke daneben hervor.
    /// </summary>
    private void AlignStickyHeader(FrameworkElement container)
    {
        if (container is not ContentPresenter cp || cp.ContentTemplate?.FindName("GroupBox", cp) is not FrameworkElement box || !box.IsVisible
            || StickyHeader.Parent is not UIElement cell) return;
        var cellLeft = ListScroll.TranslatePoint(new Point(0, 0), cell).X;
        var left = box.TranslatePoint(new Point(0, 0), cell).X - cellLeft;
        var right = ListScroll.ActualWidth - left - box.ActualWidth;
        if (left < 0 || right < 0) return;
        var margin = new Thickness(Math.Round(left), 0, Math.Round(right), 0);
        if (StickyHeader.Margin != margin) StickyHeader.Margin = margin;
    }

    private void ScrollToGroup(GroupViewModel group)
    {
        // Nach dem Aufklappen erst das Layout abwarten
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _groupContainers = null;
            var element = GroupContainers().FirstOrDefault(x => ReferenceEquals(x.Group, group)).Element;
            if (element == null) return;
            var top = element.TranslatePoint(new Point(0, 0), ListScroll).Y;
            ListScroll.ScrollToVerticalOffset(Math.Max(0, ListScroll.VerticalOffset + top));
            if (!Vm.ShowGroupJump && !SearchBox.IsKeyboardFocusWithin) SearchBox.Focus();
        });
    }

    private void GroupJump_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { GroupJumpBox.Focus(); Keyboard.Focus(GroupJumpBox); });
        else if (IsOpen) SearchBox.Focus();
    }

    private void GroupJumpBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Vm.MoveGroupJumpSelection(1); GroupJumpList.ScrollIntoView(Vm.GroupJumpSelected); e.Handled = true; break;
            case Key.Up: Vm.MoveGroupJumpSelection(-1); GroupJumpList.ScrollIntoView(Vm.GroupJumpSelected); e.Handled = true; break;
            case Key.Enter: Vm.ConfirmGroupJump(); e.Handled = true; break;
        }
    }

    private void GroupJumpList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Vm.GroupJumpSelected != null) Vm.ConfirmGroupJump();
    }

    /// <summary>Für den Selbsttest: Liste auf eine Position scrollen.</summary>
    internal void ScrollListForTest(double offset)
    {
        ListScroll.ScrollToVerticalOffset(offset);
        ListScroll.UpdateLayout();
        UpdateStickyGroup();
    }

    /// <summary>Für den Selbsttest: Abweichung (px) zwischen fixiertem Kopf und Karte der Gruppe, links plus Breite.</summary>
    internal double? StickyMisalignmentForTest()
    {
        if (Vm.StickyGroup is not { } g || GroupContainers().FirstOrDefault(x => ReferenceEquals(x.Group, g)).Element is not ContentPresenter cp
            || cp.ContentTemplate?.FindName("GroupBox", cp) is not FrameworkElement box) return null;
        var sticky = StickyHeader.TranslatePoint(new Point(0, 0), ListScroll).X;
        var boxLeft = box.TranslatePoint(new Point(0, 0), ListScroll).X;
        return Math.Abs(sticky - boxLeft) + Math.Abs(StickyHeader.ActualWidth - box.ActualWidth);
    }
}
