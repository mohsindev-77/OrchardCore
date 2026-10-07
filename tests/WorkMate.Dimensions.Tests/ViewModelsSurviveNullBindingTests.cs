using System.Reflection;
using FluentAssertions;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// Every view model survives having its string properties set to null, which is what model
/// binding does to an empty text box.
/// </summary>
/// <remarks>
/// Written after six HTTP 500s that were all the same defect in two files. MVC's
/// <c>ComplexObjectModelBinder</c> reads every public getter on the model while it is binding it,
/// and <c>ModelMetadata.ConvertEmptyStringToNull</c> defaults to <see langword="true"/>, so an
/// empty <c>&lt;input&gt;</c> arrives as <see langword="null"/> and not as an empty string — no
/// matter what the property's initialiser says, and no matter what the nullable annotations claim,
/// because the binder sets the property by reflection and neither applies.
///
/// A computed property such as <c>Name =&gt; new(NameEn.Trim(), NameAr.Trim())</c> therefore threw
/// <em>inside the binder</em>, which is before the action method, before <c>ModelState</c> and
/// before any validation: a blank name box produced a stack trace rather than a message under the
/// field. That is the specific failure this guards, and it guards the whole class of it rather
/// than the two instances that were found — a new view model with the same shape fails here on the
/// day it is written.
///
/// Reflection rather than a test per view model for the same reason <c>ConcurrencyCheckedSaveTests</c>
/// reads the source: the rule is about every member of a category, and a list of examples is a list
/// somebody has to remember to add to.
/// </remarks>
public sealed class ViewModelsSurviveNullBindingTests
{
    public static TheoryData<Type> ViewModelTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(WorkMate.Dimensions.ViewModels.StructureEditViewModel).Assembly
            .GetTypes()
            .Where(type =>
                type.Namespace == "WorkMate.Dimensions.ViewModels" &&
                type is { IsClass: true, IsAbstract: false, IsPublic: true } &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ViewModelTypes))]
    public void EveryGetterSurvivesANullStringProperty(Type viewModelType)
    {
        var model = Activator.CreateInstance(viewModelType);

        model.Should().NotBeNull();

        var settableStrings = viewModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string) && property.CanWrite)
            .ToList();

        // Nothing to null out means nothing to prove, not a pass worth reporting.
        if (settableStrings.Count == 0)
        {
            return;
        }

        foreach (var property in settableStrings)
        {
            property.SetValue(model, null);
        }

        var failures = new List<string>();

        // Every getter, not only the computed ones: the binder reads them all, and which of them
        // happens to dereference another property is exactly what nobody can be expected to track.
        foreach (var property in viewModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
        {
            try
            {
                property.GetValue(model);
            }
            catch (TargetInvocationException exception)
            {
                failures.Add($"{property.Name}: {exception.InnerException?.GetType().Name}");
            }
        }

        failures.Should().BeEmpty(
            "model binding sets a string property to null for an empty input, and a getter that "
            + "throws on it fails inside the binder — before the action, before ModelState, and "
            + "with no way to show the user a field error instead of a 500");
    }
}
