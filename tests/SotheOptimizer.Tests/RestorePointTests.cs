using Xunit;

namespace Sothe.Tests
{
    // El script real no se ejecuta aqui (haria falta ser administrador y crearia un punto de restauracion de verdad):
    // se comprueba que respeta el valor original de SystemRestorePointCreationFrequency y que lo restaura siempre.
    public class RestorePointTests
    {
        [Fact]
        public void ElScript_GuardaElValorOriginalAntesDeCambiarlo()
        {
            var s = SotheEngine.RestorePointScript;
            Assert.True(s.IndexOf("$old =", System.StringComparison.Ordinal) < s.IndexOf("-Value 0", System.StringComparison.Ordinal));
        }

        [Fact]
        public void ElScript_RestauraEnUnFinally_OBorraSiNoExistia()
        {
            var s = SotheEngine.RestorePointScript;
            var finallyPart = s.Substring(s.IndexOf("finally", System.StringComparison.Ordinal));
            Assert.Contains("-Value $old", finallyPart);
            Assert.Contains("Remove-ItemProperty", finallyPart);
        }

        [Fact]
        public void ElScript_CreaElPuntoConElNombreDeToji()
        {
            Assert.Contains("Checkpoint-Computer -Description 'Toji'", SotheEngine.RestorePointScript);
        }
    }
}
