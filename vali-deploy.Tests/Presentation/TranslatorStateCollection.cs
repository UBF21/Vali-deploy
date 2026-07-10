namespace vali_deploy.Tests.Presentation;

/// <summary>
/// Marker collection that forces every test class decorated with <c>[Collection("Translator state")]</c>
/// to run sequentially relative to each other. xUnit runs different test classes in parallel by default
/// (each in its own collection), which caused a race on <see cref="vali_deploy.Presentation.Translator"/>'s
/// static <c>_currentLanguage</c> field once more than one test class started mutating it via
/// <see cref="vali_deploy.Presentation.Translator.SetLanguage"/>. Classes outside this collection are
/// unaffected and still run in parallel with everything else.
/// </summary>
[CollectionDefinition("Translator state")]
public class TranslatorStateCollection
{
}
