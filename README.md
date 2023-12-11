# MoorhuhnSDP

"Moorhuhn Adventure - Der Schatz des Pharaos" (internationally released as "Crazy Chicken Adventure - The Pharao's Treasure") is a point-and-click adventure in the universe of Crazy Chicken.

This repos contains two tools regarding the assets of this game. However these are just proof-of-concept during reverse engineering, as paths are currently hard-coded, there are no binaries and you have to compile the code itself.
Any recent .NET SDK should suffice for this.

## ExtractArchiveCs

Extracts the `mha.dat` file which contains all actual asset files as a single, partially compressed and/or scrambled archive.

## ConvertIt2

The asset file with extension `_it2` contain images in either JPEG or a simple RLE-based format alongside some meta information about sub-images and font characters. This tool converts the full image to PNG.
