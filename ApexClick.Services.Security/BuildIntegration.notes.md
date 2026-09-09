# Release obfuscation

CI is the mandatory release gate. `.config/dotnet-tools.json` pins `Obfuscar.GlobalTool` 2.2.50.
`eng/Obfuscate-Release.ps1` generates an absolute-path Obfuscar v3-compatible config and
runs `obfuscar.console`. The GitHub Actions release job builds Release, obfuscates the
launcher, runs tests, and uploads the obfuscated artifact.

Do not make local Debug builds depend on Obfuscar. Production release tags do.
