// CliTests.Ejecutar_ComandoTokensSinArchivo_Devuelve1YElMensajeEnStderr borra transitoriamente
// docs/ui-v2/tokens.json (y lo restaura en un finally). TokensSpecTests, LectorTokensTests,
// GeneradorTokensTests y ArchivosGeneradosSincronizadosTests leen ese mismo archivo en un
// inicializador estático. Sin esto, xUnit puede correr esas clases (colecciones distintas) en
// paralelo con CliTests y toparse con el archivo borrado a mitad de camino.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
