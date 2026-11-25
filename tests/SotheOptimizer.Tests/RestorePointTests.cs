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

        // Bug real: la unidad se pasaba como "C:\\" (dos barras) y Enable-ComputerRestore no la encontraba
        [Fact]
        public void ElScript_PasaLaUnidadConUnaSolaBarra()
        {
            var s = SotheEngine.RestorePointScript;
            Assert.Contains("$d = $env:SystemDrive + '\\'; Enable-ComputerRestore -Drive $d", s);
            Assert.DoesNotContain("\\\\", s);
        }

        [Fact]
        public void ElScript_FallaConMotivoClaro_EnVezDeDarloPorCreado()
        {
            var s = SotheEngine.RestorePointScript;
            Assert.StartsWith("$ErrorActionPreference = 'Stop'", s);
            Assert.Contains("-WarningAction Stop", s);                 // el aviso de "demasiado pronto" pasa a ser error
            Assert.Contains("(VSS) is disabled", s);                   // servicio de instantaneas desactivado
            Assert.Contains("Get-ComputerRestorePoint", s);            // se comprueba que el punto existe de verdad
        }

        [Fact]
        public void SinIntentoPrevio_NoHayMotivoDeFallo()
        {
            using var d = new TestDirs();
            Assert.Null(d.Engine().LastRestorePointError);
        }
    }
}
