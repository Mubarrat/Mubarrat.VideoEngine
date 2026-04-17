using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;
using System.Collections.ObjectModel;

namespace Mubarrat.VideoEngine.Objects;

public abstract class Panel : FrameworkObject
{
    private readonly ChildCollection children;

    protected Panel() => children = new ChildCollection(this);

    public IBrush? Background { get => (IBrush?)this[BackgroundProperty]; set => this[BackgroundProperty] = value; }
    public static readonly Property BackgroundProperty = new(nameof(Background), typeof(IBrush), AffectsArrange: true);

    public ObservableCollection<FrameworkObject> Children => children;

    protected override IEnumerable<FrameworkObject> ChildrenIterator => children;

    public override Drawing ToDrawing()
    {
        List<Drawing> drawings = new(children.Count + 1);

        if (Background is not null && ActualBounds.Width > 0 && ActualBounds.Height > 0)
        {
            drawings.Add(new PathDrawing
            {
                Path = PathBuilder.Rectangle(new Rect(0, 0, ActualBounds.Width, ActualBounds.Height)).Build(),
                Fill = Background,
                Stroke = default
            });
        }

        foreach (var child in ChildrenIterator)
            drawings.Add(child.ToDrawing());

        return new GroupDrawing
        {
            Drawings = drawings,
            Transform = LayoutTransform * RenderTransform,
            Opacity = Opacity,
            Name = Name
        };
    }

    private sealed class ChildCollection(Panel owner) : ObservableCollection<FrameworkObject>
    {
        protected override void InsertItem(int index, FrameworkObject item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (ReferenceEquals(item.Parent, owner) && Contains(item))
                return;

            if (item.Parent is Panel previousPanel && !ReferenceEquals(previousPanel, owner))
                previousPanel.Children.Remove(item);
            else if (item.Parent is not null && !ReferenceEquals(item.Parent, owner))
                item.Parent = null;

            if (!ReferenceEquals(item.Parent, owner))
                item.Parent = owner;

            base.InsertItem(index, item);
            owner.InvalidateMeasure();
        }

        protected override void SetItem(int index, FrameworkObject item)
        {
            ArgumentNullException.ThrowIfNull(item);

            var oldItem = this[index];
            if (ReferenceEquals(oldItem, item))
                return;

            if (item.Parent is Panel previousPanel && !ReferenceEquals(previousPanel, owner))
                previousPanel.Children.Remove(item);
            else if (item.Parent is not null && !ReferenceEquals(item.Parent, owner))
                item.Parent = null;

            if (!ReferenceEquals(item.Parent, owner))
                item.Parent = owner;

            base.SetItem(index, item);

            if (ReferenceEquals(oldItem.Parent, owner))
                oldItem.Parent = null;

            owner.InvalidateMeasure();
        }

        protected override void RemoveItem(int index)
        {
            var oldItem = this[index];
            base.RemoveItem(index);

            if (ReferenceEquals(oldItem.Parent, owner))
                oldItem.Parent = null;

            owner.InvalidateMeasure();
        }

        protected override void ClearItems()
        {
            List<FrameworkObject> snapshot = [.. this];
            base.ClearItems();

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (ReferenceEquals(snapshot[i].Parent, owner))
                    snapshot[i].Parent = null;
            }

            owner.InvalidateMeasure();
        }
    }
}
