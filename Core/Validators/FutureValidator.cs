using FluentValidation;
using Terminal.Core.Models;

namespace Terminal.Core.Validators
{
  /// <summary>
  /// Validation rules
  /// </summary>
  public class FutureValidator : AbstractValidator<FutureModel?>
  {
    public FutureValidator()
    {
      RuleFor(o => o.Value).NotEmpty().SetValidator(new FutureValueValidator());
    }
  }

  /// <summary>
  /// Validation rules
  /// </summary>
  public class FutureValueValidator : AbstractValidator<FutureModel>
  {
    public FutureValueValidator()
    {
      RuleFor(o => o.Name).NotEmpty();
      RuleFor(o => o.ExpirationDate).NotEmpty();
      RuleFor(o => o.Instrument).NotEmpty().SetValidator(new InstrumentValidator());
    }
  }
}
