using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEngine;
using System;

public class Vector3Converter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Vector3 vector = (Vector3)value;
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(vector.x);
        writer.WritePropertyName("y");
        writer.WriteValue(vector.y);
        writer.WritePropertyName("z");
        writer.WriteValue(vector.z);
        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject obj = JObject.Load(reader);
        float x = (float)obj["x"];
        float y = (float)obj["y"];
        float z = (float)obj["z"];
        return new Vector3(x, y, z);
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Vector3);
    }
}

public class ColorConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Color color = (Color)value;
        writer.WriteStartObject();
        writer.WritePropertyName("r");
        writer.WriteValue(color.r);
        writer.WritePropertyName("g");
        writer.WriteValue(color.g);
        writer.WritePropertyName("b");
        writer.WriteValue(color.b);
        writer.WritePropertyName("a");
        writer.WriteValue(color.a);
        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject obj = JObject.Load(reader);
        float r = (float)obj["r"];
        float g = (float)obj["g"];
        float b = (float)obj["b"];
        float a = (float)obj["a"];
        return new Color(r, g, b, a);
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Color);
    }
}

public class QuaternionConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Quaternion quaternion = (Quaternion)value;
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(quaternion.x);
        writer.WritePropertyName("y");
        writer.WriteValue(quaternion.y);
        writer.WritePropertyName("z");
        writer.WriteValue(quaternion.z);
        writer.WritePropertyName("w");
        writer.WriteValue(quaternion.w);
        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject obj = JObject.Load(reader);
        float x = (float)obj["x"];
        float y = (float)obj["y"];
        float z = (float)obj["z"];
        float w = (float)obj["w"];
        return new Quaternion(x, y, z, w);
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Quaternion);
    }
}

public class Vector2Converter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Vector2 vector = (Vector2)value;
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(vector.x);
        writer.WritePropertyName("y");
        writer.WriteValue(vector.y);
        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject obj = JObject.Load(reader);
        float x = (float)obj["x"];
        float y = (float)obj["y"];
        return new Vector2(x, y);
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Vector2);
    }
}