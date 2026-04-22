using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Linq;

namespace MMONavigator.Helpers;

public static class Methods {
    public static string GetDisplayName(Enum enumValue)
    {
        return enumValue.GetType()
            .GetMember(enumValue.ToString())
            .First()
            .GetCustomAttribute<DisplayAttribute>()?
            .GetName() ?? enumValue.ToString();
    }
}