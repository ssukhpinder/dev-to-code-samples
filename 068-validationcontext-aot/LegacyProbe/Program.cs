using System.ComponentModel.DataAnnotations;

var options = new CheckoutOptions();
var context = new ValidationContext(options);

Console.WriteLine(context.DisplayName);

internal sealed class CheckoutOptions;
