# Windows rendering validation

Use a disposable Unity 6000.5 project referencing this package with the Starter sample imported.
Copy ReleaseValidation.cs into Assets/Editor and ReleasePlayerValidation.cs into Assets.
Run Unity with -batchmode -quit -executeMethod ReleaseValidation.Build -force-d3d11.
The build creates a URP 2D pipeline and writes Build/ReleaseValidation.exe.
Run that player with -batchmode -force-d3d11. It writes player-result.txt and three
320x180 RGBA captures beside the executable: unlit, lit at intensity 1, and lit at intensity 0.
The harness explicitly submits camera render requests because hidden/batch players may not
present regular camera frames. A failing pixel assertion exits the player with code 1.
This helper creates validation assets; use it only in a disposable project.
