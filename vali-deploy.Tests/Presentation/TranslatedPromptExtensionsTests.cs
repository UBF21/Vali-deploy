using vali_deploy.Presentation;

namespace vali_deploy.Tests.Presentation;

[Collection("Translator state")]
public class TranslatedPromptExtensionsTests
{
    [Fact]
    public void FormatTranslatedTitle_substitutes_args_into_spanish_template_when_language_is_spanish()
    {
        try
        {
            Translator.SetLanguage("es");

            var result = TranslatedPromptExtensions.FormatTranslatedTitle(
                "Select a subproject for project '{0}' to manage files to omit", "shop");

            Assert.Equal("Elegí un subproyecto del proyecto 'shop' para gestionar archivos a omitir", result);
        }
        finally
        {
            Translator.SetLanguage("en");
        }
    }

    [Fact]
    public void FormatTranslatedTitle_substitutes_args_into_english_template_when_language_is_english()
    {
        Translator.SetLanguage("en");

        var result = TranslatedPromptExtensions.FormatTranslatedTitle(
            "What do you want to do with subproject '{0}'?", "shop");

        Assert.Equal("What do you want to do with subproject 'shop'?", result);
    }

    [Fact]
    public void FormatTranslatedTitle_returns_original_template_formatted_when_key_not_found()
    {
        try
        {
            Translator.SetLanguage("es");

            var result = TranslatedPromptExtensions.FormatTranslatedTitle(
                "Untranslated template with '{0}'", "value");

            Assert.Equal("Untranslated template with 'value'", result);
        }
        finally
        {
            Translator.SetLanguage("en");
        }
    }
}
