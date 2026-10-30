# Política de firma de código

**Estado: pendiente.** NotchWin todavía no ha sido aceptado en SignPath Foundation, así que los
releases actuales **no están firmados**. Este documento describe cómo se firmará cuando se apruebe,
y es el texto que SignPath pide publicar.

## Por qué firmar

Smart App Control de Windows 11 bloquea los ejecutables sin firma de una entidad reconocida, y
SmartScreen advierte sobre ellos. Sin firma, quien tenga esas protecciones activas no puede ejecutar
NotchWin.

## Quién firma

Una vez aprobado el proyecto: *Free code signing provided by [SignPath.io](https://signpath.io),
certificate by [SignPath Foundation](https://signpath.org).* El editor que aparece en la firma es
SignPath Foundation.

## Roles del equipo

| Rol | Persona |
|---|---|
| Autores (suben código) | [@PipeSick](https://github.com/PipeSick) |
| Revisores | [@PipeSick](https://github.com/PipeSick) |
| Aprobadores (autorizan cada firma) | [@PipeSick](https://github.com/PipeSick) |

Cada release necesita la aprobación manual de un aprobador antes de firmarse. Todos los miembros
usan autenticación en dos pasos en GitHub y en SignPath.

## Qué se firma

Solo binarios compilados desde este repositorio, con el workflow
[`release.yml`](../.github/workflows/release.yml) en los servidores de GitHub: `NotchWin.App.exe`,
`NotchWin.App.dll` y `NotchWin.Core.dll`. Las librerías de terceros (Windows App SDK, .NET) ya vienen
firmadas por Microsoft y no se vuelven a firmar. La configuración está en
[`signpath/artifact-configuration.xml`](../signpath/artifact-configuration.xml).

Nombre de producto y versión salen de los metadatos de los proyectos (`Product`, `Version`) y son
los mismos en todos los binarios de un release.

## Privacidad

NotchWin no tiene telemetría. Solo envía datos fuera del equipo si activas las letras sincronizadas
en Ajustes: el título y el artista de la canción van a [lrclib.net](https://lrclib.net). El resto
(agentes de IA, calendario, descargas, estantería) lee archivos de tu disco y no sale de él.

## Qué hace NotchWin en tu sistema

Crea ventanas siempre visibles arriba de cada pantalla, un ícono en la bandeja y un archivo de
ajustes en `%LOCALAPPDATA%\NotchWin`. Para desinstalarlo basta con borrar la carpeta del programa y
esa carpeta de ajustes. No instala servicios ni toca el registro.
