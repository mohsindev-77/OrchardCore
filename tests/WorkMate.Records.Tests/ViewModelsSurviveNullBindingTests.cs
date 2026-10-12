using System.Reflection;
using FluentAssertions;
using WorkMate.Records.ViewModels;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// Every view model in this module survives having its string properties set to null, which is what
/// model binding does to an empty text box.
/// </summary>
/// <remarks>
/// The same guard <c>WorkMate.Dimensions.Tests.ViewModelsSurviveNullBindingTests</c> keeps, applied
/// to this module's view models. It is worth having twice rather than once: the defect it catches is
/// in a view model, the scan is over one assembly's view models, and a module without the test is a
/// module where the next one is found by a customer.
///
/// MVC's <c>ComplexObjectModelBinder</c> reads every public getter on the model while it is binding
/// it, and <c>ModelMetadata.ConvertEmptyStringToNull</c> defaults to true — so an empty
/// <c>&lt;input&gt;</c> arrives as null and not as an empty string, no matter what the property's
/// initialiser says and no matter what the nullable annotations claim, because the binder sets the
/// property by reflection and neither applies.
///
/// A computed property such as <c>Name =&gt; new(NameEn.Trim(), NameAr.Trim())</c> therefore throws
/// <em>inside the binder</em>, which is before the action method, before <c>ModelState</c> and
/// before any validation: a blank name box produces a stack trace rather than a message under the
/// field. ADR-0003's addendum records the six HTTP 500s that cost. <c>LookupListEditViewModel.Name</c>
/// and <c>ToItems()</c> are exactly that shape, which is why this arrives with them.
///
/// Reflection rather than a test per view model, for the reason the source-reading guards give: the
/// rule is about every member of a category, and a list of examples is a list somebody has to
/// remember to add to.
/// </remarks>
public sealed class ViewModelsSurviveNullBindingTests
{
    public static TheoryData<Type> ViewModelTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(LookupListEditViewModel).Assembly
            .GetTypes()
            .Where(type =>
                type.Namespace == "WorkMate.Records.ViewModels" &&
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

    /// <summary>
    /// The one method on a view model that is not a getter, and that the controller calls on a form
    /// a reader may have left entirely blank.
    /// </summary>
    /// <remarks>
    /// <c>ToItems()</c> is not reached by the reflection above, because it is a method rather than a
    /// property — and it is the place in this module most likely to have the defect, since it reads
    /// three nullable strings off every row. A blank row is the ordinary case: pressing "Add an
    /// entry" produces one.
    /// </remarks>
    [Fact]
    public void TheEntriesOfAnEmptyFormCanStillBeRead()
    {
        var model = new LookupListEditViewModel
        {
            Items = [new LookupListItemRowViewModel(), new LookupListItemRowViewModel()],
        };

        var items = model.ToItems();

        items.Should().HaveCount(2);
        items.Should().AllSatisfy(item =>
        {
            item.Code.Should().BeEmpty();
            item.Name.En.Should().BeEmpty();
            item.Name.Ar.Should().BeEmpty();
        });
    }
}
