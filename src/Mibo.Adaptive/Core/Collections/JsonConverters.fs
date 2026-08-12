namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization

/// <summary>
/// System.Text.Json converter for <see cref="ChangeableValue&lt;'T&gt;"/>: the
/// value as a plain JSON scalar. Deserialization rebuilds the node through its
/// constructor, attaching it to the deserializing thread's ambient graph.
/// </summary>
type internal ChangeableValueConverter<'T>() =
  inherit JsonConverter<ChangeableValue<'T>>()

  override _.Read
    (
      reader: byref<Utf8JsonReader>,
      _typeToConvert: Type,
      _options: JsonSerializerOptions
    ) =
    if reader.TokenType = JsonTokenType.Null then
      raise(JsonException "JSON null cannot deserialize into an adaptive node.")
    else
      ChangeableValue(JsonSerializer.Deserialize<'T>(&reader))

  override _.Write
    (
      writer: Utf8JsonWriter,
      value: ChangeableValue<'T>,
      _options: JsonSerializerOptions
    ) =
    JsonSerializer.Serialize(writer, value.Value)

/// <summary>
/// System.Text.Json converter for <see cref="ChangeableSet&lt;'T&gt;"/>: the
/// elements as a plain JSON array. See <see cref="ChangeableValueConverter&lt;'T&gt;"/>
/// for the deserialization contract.
/// </summary>
type internal ChangeableSetConverter<'T>() =
  inherit JsonConverter<ChangeableSet<'T>>()

  override _.Read
    (
      reader: byref<Utf8JsonReader>,
      _typeToConvert: Type,
      _options: JsonSerializerOptions
    ) =
    if reader.TokenType = JsonTokenType.Null then
      raise(JsonException "JSON null cannot deserialize into an adaptive node.")
    else
      new ChangeableSet<'T>(JsonSerializer.Deserialize<HashSet<'T>>(&reader))

  override _.Write
    (
      writer: Utf8JsonWriter,
      value: ChangeableSet<'T>,
      _options: JsonSerializerOptions
    ) =
    JsonSerializer.Serialize(writer, (value :> IAdaptiveSet<'T>).GetValue())

/// <summary>
/// System.Text.Json converter for <see cref="ChangeableMap&lt;'K,'V&gt;"/>: the
/// entries as a plain JSON object. See <see cref="ChangeableValueConverter&lt;'T&gt;"/>
/// for the deserialization contract.
/// </summary>
type internal ChangeableMapConverter<'K, 'V when 'K: equality>() =
  inherit JsonConverter<ChangeableMap<'K, 'V>>()

  override _.Read
    (
      reader: byref<Utf8JsonReader>,
      _typeToConvert: Type,
      _options: JsonSerializerOptions
    ) =
    if reader.TokenType = JsonTokenType.Null then
      raise(JsonException "JSON null cannot deserialize into an adaptive node.")
    else
      let d = JsonSerializer.Deserialize<Dictionary<'K, 'V>>(&reader)

      new ChangeableMap<'K, 'V>(seq { for kv in d -> kv.Key, kv.Value })

  override _.Write
    (
      writer: Utf8JsonWriter,
      value: ChangeableMap<'K, 'V>,
      _options: JsonSerializerOptions
    ) =
    JsonSerializer.Serialize(writer, (value :> IAdaptiveMap<'K, 'V>).GetValue())

/// <summary>
/// System.Text.Json converter for <see cref="ChangeableList&lt;'T&gt;"/>: the
/// elements as a plain JSON array. See <see cref="ChangeableValueConverter&lt;'T&gt;"/>
/// for the deserialization contract.
/// </summary>
type internal ChangeableListConverter<'T>() =
  inherit JsonConverter<ChangeableList<'T>>()

  override _.Read
    (
      reader: byref<Utf8JsonReader>,
      _typeToConvert: Type,
      _options: JsonSerializerOptions
    ) =
    if reader.TokenType = JsonTokenType.Null then
      raise(JsonException "JSON null cannot deserialize into an adaptive node.")
    else
      new ChangeableList<'T>(JsonSerializer.Deserialize<List<'T>>(&reader))

  override _.Write
    (
      writer: Utf8JsonWriter,
      value: ChangeableList<'T>,
      _options: JsonSerializerOptions
    ) =
    JsonSerializer.Serialize(writer, (value :> IAdaptiveList<'T>).GetValue())
