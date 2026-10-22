# Third-Party Notices

## Mono.Cecil

This project embeds `lib/Mono.Cecil.dll` to read and write .NET assemblies.

- Project: Mono.Cecil
- Copyright: Copyright (c) 2008 - 2015 Jb Evain
- Copyright: Copyright (c) 2008 - 2011 Novell, Inc.
- License: MIT

The full license text is included in `src/CECIL-LICENSE.txt` and is embedded in the single-file build.

```text
Copyright (c) 2008 - 2015 Jb Evain
Copyright (c) 2008 - 2011 Novell, Inc.

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## .NET Framework

The build targets .NET Framework 4.8 and uses Microsoft Windows Forms,
System.Drawing, System.IO.Compression and related framework assemblies.
These runtime libraries are not vendored in this repository.

## PCL2, PCL2 CE, and HMCL

PCL2, PCL2 CE, and HMCL are separate third-party launcher projects.
This repository does not contain their source code or binary distributions.
The tool only reads and modifies local copies selected by the user. Their
names and trademarks belong to their respective owners.
