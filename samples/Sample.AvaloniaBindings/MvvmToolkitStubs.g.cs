namespace CommunityToolkit.Mvvm.ComponentModel
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ObservablePropertyAttribute : Attribute;
}

namespace CommunityToolkit.Mvvm.Input
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RelayCommandAttribute : Attribute;
}
