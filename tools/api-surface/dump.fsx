// Dumps the public API surface of one assembly to a sorted text file so
// releases can be diffed for accidental breaks.
//
//   dotnet fsi dump.fsx -- <binDir> <assemblyName> <outFile>
//
// The dump uniones reflection names (types, methods, properties, fields,
// events — including parameter type names) with the names in the generated
// XML documentation file, because F# type abbreviations (e.g.
// `type Dispatch<'Msg> = 'Msg -> unit`) have no runtime identity and are
// invisible to reflection but do appear in the XML docs.

open System
open System.IO
open System.Reflection
open System.Text.RegularExpressions

let binDir = fsi.CommandLineArgs.[1]
let asmName = fsi.CommandLineArgs.[2]
let outFile = fsi.CommandLineArgs.[3]

let resolveAssembly (_: obj) (e: ResolveEventArgs) =
  let name = AssemblyName(e.Name).Name

  let candidate =
    [ Path.Combine(binDir, name + ".dll"); Path.Combine(binDir, name + ".exe") ]
    |> List.tryFind File.Exists

  candidate |> Option.map Assembly.LoadFrom |> Option.toObj

AppDomain.CurrentDomain.add_AssemblyResolve(
  ResolveEventHandler(resolveAssembly)
)

let lines = ResizeArray<string>()
let asm = Assembly.LoadFrom(Path.Combine(binDir, asmName + ".dll"))

let flags =
  BindingFlags.Public
  ||| BindingFlags.Instance
  ||| BindingFlags.Static
  ||| BindingFlags.DeclaredOnly

let rec visit(t: Type) =
  lines.Add("T: " + t.FullName)

  for m in t.GetMethods(flags) do
    let ps =
      m.GetParameters()
      |> Array.map(fun p -> p.ParameterType.Name)
      |> String.concat ","

    lines.Add($"M: {t.FullName}.{m.Name}({ps}):{m.ReturnType.Name}")

  for p in t.GetProperties(flags) do
    lines.Add($"P: {t.FullName}.{p.Name}:{p.PropertyType.Name}")

  for f in t.GetFields(flags) do
    lines.Add($"F: {t.FullName}.{f.Name}:{f.FieldType.Name}")

  for e in t.GetEvents(flags) do
    lines.Add($"E: {t.FullName}.{e.Name}")

  for n in t.GetNestedTypes(flags) do
    visit n

for t in asm.GetExportedTypes() do
  visit t

// Union the generated XML doc type names (covers F# abbreviations, whose
// members are plain functions and already visible to reflection).
let xmlPath = Path.Combine(binDir, asmName + ".xml")

if File.Exists xmlPath then
  let rx = Regex(@"name=""T:(?<name>[^""]+)""")

  for line in File.ReadLines(xmlPath) do
    let m = rx.Match(line)

    if m.Success then
      lines.Add("T: " + m.Groups.["name"].Value)

let sorted = lines |> Seq.distinct |> Seq.sort

File.WriteAllLines(outFile, sorted)
printfn $"wrote {outFile} ({Seq.length sorted} names)"
