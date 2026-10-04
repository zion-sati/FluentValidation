using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Resources;
using FluentValidation.Results;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.FluentValidation.Tests;

public sealed partial class ValidationCompatibilityTests
{
    [Test]
    public async Task RequiredLengthAndCustomMessagePreservePropertyAndErrorCode()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required")
            .WithErrorCode("name.required")
            .Length(2, 12);

        var result = validator.Validate(new Registration { Name = "" });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("Name");
        await Assert.That(result.Errors[0].ErrorCode).IsEqualTo("name.required");
        await Assert.That(result.Errors[0].ErrorMessage).IsEqualTo("Name is required");
    }

    [Test]
    public async Task NumericRangeAndCrossPropertyComparisonValidateValues()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Age).InclusiveBetween(18, 65);
        validator.RuleFor(x => x.ConfirmAge).Equal(x => x.Age);

        var invalid = validator.Validate(new Registration { Age = 17, ConfirmAge = 18 });
        var valid = validator.Validate(new Registration { Age = 30, ConfirmAge = 30 });

        await Assert.That(invalid.IsValid).IsFalse();
        await Assert.That(invalid.Errors.Count).IsEqualTo(2);
        await Assert.That(invalid.Errors[0].PropertyName).IsEqualTo("Age");
        await Assert.That(invalid.Errors[1].PropertyName).IsEqualTo("ConfirmAge");
        await Assert.That(valid.IsValid).IsTrue();
    }

    [Test]
    public async Task InterpretedEqualityExpressionCanDriveAModelLevelRule()
    {
        Expression<Func<Registration, bool>> predicate = model => model.Name == "Ada";
        var matchesAda = predicate.Compile(preferInterpretation: true);
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(model => model).Must(matchesAda);

        var valid = validator.Validate(new Registration { Name = "Ada" });
        var invalid = validator.Validate(new Registration { Name = "Grace" });

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(invalid.IsValid).IsFalse();
        await Assert.That(invalid.Errors[0].PropertyName).IsEqualTo("");
    }

    [Test]
    public async Task NestedPropertiesAndNullParentsKeepStablePropertyPaths()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Address!.City).NotEmpty().When(x => x.Address != null);

        var nullParent = validator.Validate(new Registration { Address = null });
        var invalidCity = validator.Validate(new Registration { Address = new Address { City = "" } });

        await Assert.That(nullParent.IsValid).IsTrue();
        await Assert.That(invalidCity.IsValid).IsFalse();
        await Assert.That(invalidCity.Errors[0].PropertyName).IsEqualTo("Address.City");
    }

    [Test]
    public async Task NullableAndNestedValuePropertiesAreReadByAccessors()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.OptionalAge).GreaterThan(0);
        validator.RuleFor(x => x.Profile!.Score).GreaterThan(0);

        var result = validator.Validate(new Registration
        {
            OptionalAge = -1,
            Profile = new Profile { Score = -2 }
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("OptionalAge");
        await Assert.That(result.Errors[1].PropertyName).IsEqualTo("Profile.Score");
    }

    [Test]
    public async Task PublicFieldsNestedStructsAndBoxingConversionsRemainUsable()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.PublicCode).NotEmpty();
        validator.RuleFor(x => x.Location.ZipCode).NotEmpty();
        validator.RuleFor(x => (object)x.Age).NotNull();

        var result = validator.Validate(new Registration
        {
            PublicCode = "",
            Location = new MailingLocation { ZipCode = "" },
            Age = 21
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("PublicCode");
        await Assert.That(result.Errors[1].PropertyName).IsEqualTo("Location.ZipCode");
    }

    [Test]
    public async Task RuleForEachReportsCollectionIndexesAndNestedFailures()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.Code).NotEmpty();
        });

        var result = validator.Validate(new Registration
        {
            Items = new List<Item> { new() { Code = "ok" }, new() { Code = "" } }
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(1);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("Items[1].Code");
    }

    [Test]
    public async Task CollectionPropertySelectionMatchesIndexedChildPaths()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.Code).NotEmpty();
        });

        var result = validator.Validate(
            new Registration { Items = new List<Item> { new() { Code = "" }, new() { Code = "" } } },
            options => options.IncludeProperties("Items[].Code"));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("Items[0].Code");
        await Assert.That(result.Errors[1].PropertyName).IsEqualTo("Items[1].Code");
    }

    [Test]
    public async Task ConditionsCascadeAndDependentRulesPreserveOrder()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("required")
            .Must(value => value.Length >= 3).WithMessage("too short");
        validator.When(x => x.CheckCode, () =>
        {
            validator.RuleFor(x => x.Code).NotEmpty().WithMessage("code required");
        });
        validator.RuleFor(x => x.Age).GreaterThan(0).DependentRules(() =>
        {
            validator.RuleFor(x => x.ConfirmAge).Equal(x => x.Age).WithMessage("age mismatch");
        });

        var result = validator.Validate(new Registration { Name = "", CheckCode = true, Code = "", Age = 0 });

        await Assert.That(result.Errors.Count).IsEqualTo(3);
        await Assert.That(result.Errors[0].ErrorMessage).IsEqualTo("required");
        await Assert.That(result.Errors[1].ErrorMessage).IsEqualTo("code required");
        await Assert.That(result.Errors[2].PropertyName).IsEqualTo("Age");
    }

    [Test]
    public async Task CustomPredicateIncludesStateSeverityAndErrorCode()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Code)
            .Must(code => code == "approved")
            .WithMessage("Code {PropertyValue} is not approved")
            .WithErrorCode("code.invalid")
            .WithSeverity(Severity.Warning)
            .WithState(_ => "review");

        var result = validator.Validate(new Registration { Code = "pending" });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors[0].ErrorMessage).IsEqualTo("Code pending is not approved");
        await Assert.That(result.Errors[0].ErrorCode).IsEqualTo("code.invalid");
        await Assert.That(result.Errors[0].Severity).IsEqualTo(Severity.Warning);
        await Assert.That((string)result.Errors[0].CustomState!).IsEqualTo("review");
    }

    [Test]
    public async Task ModelLevelRulesAndIncludedValidatorsRunTogether()
    {
        var validator = new InlineValidator<Registration>();
        validator.Include(new AddressValidator());
        validator.RuleFor(x => x).Must(x => x.Age >= x.MinimumAge).WithMessage("Age is below minimum");

        var result = validator.Validate(new Registration
        {
            Age = 16,
            MinimumAge = 18,
            Address = new Address { City = "" }
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("Address.City");
        await Assert.That(result.Errors[1].PropertyName).IsEqualTo("");
    }

    [Test]
    public async Task ExplicitPolymorphicValidatorsRunForRegisteredSubtypes()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Payment).SetInheritanceValidator(subtypes =>
        {
            subtypes.Add<CardPayment>(new CardPaymentValidator());
            subtypes.Add<WirePayment>(new WirePaymentValidator());
        });

        var result = validator.Validate(new Registration
        {
            Payment = new CardPayment { Number = "" }
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(1);
        await Assert.That(result.Errors[0].PropertyName).IsEqualTo("Payment.Number");
    }

    [Test]
    public async Task RuleSetSelectionKeepsDefaultAndNamedRulesSeparate()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Code).NotEmpty().WithMessage("default rule");
        validator.RuleSet("Create", () =>
        {
            validator.RuleFor(x => x.Name).NotEmpty().WithMessage("create rule");
        });

        var model = new Registration { Name = "", Code = "" };
        var defaults = validator.Validate(model);
        var create = validator.Validate(model, options => options.IncludeRuleSets("Create"));

        await Assert.That(defaults.Errors.Count).IsEqualTo(1);
        await Assert.That(defaults.Errors[0].ErrorMessage).IsEqualTo("default rule");
        await Assert.That(create.Errors.Count).IsEqualTo(1);
        await Assert.That(create.Errors[0].ErrorMessage).IsEqualTo("create rule");
    }

    [Test]
    public async Task LanguageManagerUsesCultureFallbackCustomTranslationsAndEnglishDefault()
    {
        var manager = new LanguageManager { Culture = new CultureInfo("fr-FR") };

        var frenchFallback = manager.GetString("NotNullValidator");
        manager.AddTranslation("fr-FR", "NotNullValidator", "custom translation");
        var custom = manager.GetString("NotNullValidator");
        manager.Enabled = false;
        var english = manager.GetString("NotNullValidator");

        await Assert.That(frenchFallback).IsEqualTo("'{PropertyName}' ne doit pas avoir la valeur null.");
        await Assert.That(custom).IsEqualTo("custom translation");
        await Assert.That(english).IsEqualTo("'{PropertyName}' must not be empty.");
    }

    [Test]
    public async Task AsyncPredicateRunsAndReceivesCancellationToken()
    {
        var tokenObserved = false;
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Code).MustAsync((code, token) =>
        {
            tokenObserved = token == CancellationToken.None;
            return Task.FromResult(code == "available");
        });

        var result = await validator.ValidateAsync(new Registration { Code = "available" });

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(tokenObserved).IsTrue();
    }

    [Test]
    public async Task AsyncPredicateObservesCancellation()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Code).MustAsync((_, token) => Task.FromCanceled<bool>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var observedCancellation = false;
        try
        {
            await validator.ValidateAsync(new Registration { Code = "value" }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            observedCancellation = true;
        }

        await Assert.That(observedCancellation).IsTrue();
    }

    [Test]
    public async Task RepeatedEquivalentRulesRemainCorrectWithAccessorCacheEnabledOrDisabled()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Name).NotEmpty();
        validator.RuleFor(x => x.Name).MinimumLength(2);
        validator.RuleFor(x => x.Items).Must(items => items.Count > 0);

        var originalSetting = ValidatorOptions.Global.DisableAccessorCache;
        ValidationResult cached;
        ValidationResult uncached;
        try
        {
            ValidatorOptions.Global.DisableAccessorCache = false;
            cached = validator.Validate(new Registration { Name = "A", Items = new List<Item>() });
            ValidatorOptions.Global.DisableAccessorCache = true;
            uncached = validator.Validate(new Registration { Name = "A", Items = new List<Item>() });
        }
        finally
        {
            ValidatorOptions.Global.DisableAccessorCache = originalSetting;
        }

        await Assert.That(cached.Errors.Count).IsEqualTo(2);
        await Assert.That(uncached.Errors.Count).IsEqualTo(2);
        await Assert.That(cached.Errors[0].PropertyName).IsEqualTo(uncached.Errors[0].PropertyName);
        await Assert.That(cached.Errors[1].PropertyName).IsEqualTo(uncached.Errors[1].PropertyName);
    }

    [Test]
    public async Task FlagsEnumAcceptsDefinedCombinationsAndRejectsUnknownBits()
    {
        var validator = new InlineValidator<Registration>();
        validator.RuleFor(x => x.Permissions).IsInEnum();

        var valid = validator.Validate(new Registration { Permissions = Permission.Read | Permission.Write });
        var invalid = validator.Validate(new Registration { Permissions = (Permission)8 });

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(invalid.IsValid).IsFalse();
        await Assert.That(invalid.Errors[0].PropertyName).IsEqualTo("Permissions");
    }

    private sealed class AddressValidator : AbstractValidator<Registration>
    {
        public AddressValidator()
        {
            RuleFor(x => x.Address!.City).NotEmpty();
        }
    }

    private sealed class Registration
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public int Age { get; set; }
        public int ConfirmAge { get; set; }
        public int MinimumAge { get; set; }
        public int? OptionalAge { get; set; }
        public Profile? Profile { get; set; }
        public Address? Address { get; set; }
        public List<Item> Items { get; set; } = new();
        public bool CheckCode { get; set; }
        public Permission Permissions { get; set; }
        public string PublicCode = "";
        public MailingLocation Location { get; set; }
        public PaymentMethod? Payment { get; set; }
    }

    private sealed class Profile
    {
        public int? Score { get; set; }
    }

    private sealed class Address
    {
        public string City { get; set; } = "";
    }

    private sealed class Item
    {
        public string Code { get; set; } = "";
    }

    private abstract class PaymentMethod
    {
    }

    private sealed class CardPayment : PaymentMethod
    {
        public string Number { get; set; } = "";
    }

    private sealed class WirePayment : PaymentMethod
    {
        public string Reference { get; set; } = "";
    }

    private sealed class CardPaymentValidator : AbstractValidator<CardPayment>
    {
        public CardPaymentValidator()
        {
            RuleFor(x => x.Number).NotEmpty();
        }
    }

    private sealed class WirePaymentValidator : AbstractValidator<WirePayment>
    {
        public WirePaymentValidator()
        {
            RuleFor(x => x.Reference).NotEmpty();
        }
    }

    private struct MailingLocation
    {
        public string? ZipCode { get; set; }
    }

    [Flags]
    private enum Permission
    {
        None = 0,
        Read = 1,
        Write = 2
    }
}
