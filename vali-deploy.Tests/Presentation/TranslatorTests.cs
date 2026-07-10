using vali_deploy.Presentation;

namespace vali_deploy.Tests.Presentation;

public class TranslatorTests
{
    [Fact]
    public void T_returns_english_unchanged_when_current_language_is_english()
    {
        Translator.SetLanguage("en");

        Assert.Equal("Add Project", Translator.T("Add Project"));
    }

    [Fact]
    public void T_returns_translation_when_current_language_is_spanish_and_key_exists()
    {
        try
        {
            Translator.SetLanguage("es");

            Assert.Equal("Agregar Proyecto", Translator.T("Add Project"));
        }
        finally
        {
            Translator.SetLanguage("en");
        }
    }

    [Fact]
    public void T_returns_original_text_unchanged_when_spanish_and_key_not_found()
    {
        try
        {
            Translator.SetLanguage("es");

            Assert.Equal("MyDynamicProjectName", Translator.T("MyDynamicProjectName"));
        }
        finally
        {
            Translator.SetLanguage("en");
        }
    }

    [Fact]
    public void SetLanguage_changes_behavior_of_subsequent_T_calls()
    {
        try
        {
            Translator.SetLanguage("en");
            Assert.Equal("Show Projects", Translator.T("Show Projects"));

            Translator.SetLanguage("es");
            Assert.Equal("Ver Proyectos", Translator.T("Show Projects"));

            Translator.SetLanguage("en");
            Assert.Equal("Show Projects", Translator.T("Show Projects"));
        }
        finally
        {
            Translator.SetLanguage("en");
        }
    }
}
