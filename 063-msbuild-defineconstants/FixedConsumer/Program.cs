using MarkerLibrary;

#if NET9_0_OR_GREATER
const string compileSymbol = "present";
#else
const string compileSymbol = "missing";
#endif

Console.WriteLine($"reference={FeatureMarker.Describe()}; compile-symbol={compileSymbol}");
