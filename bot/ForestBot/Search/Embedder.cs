using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace ForestBot.Search;

/// Text -> a normalised vector. Local, so searching sends nothing anywhere.
public interface IEmbedder
{
    /// A document (a chunk) as stored.
    float[] EmbedDocument(string text);
    /// A question, with the model's query instruction if it has one.
    float[] EmbedQuery(string text);
    /// Changes when the model does - part of the vector cache key.
    string ModelId { get; }
}

// ------------------------------------------------------------------
// BAAI bge-small-en-v1.5 (MIT, 384 dimensions, ~130 MB) through ONNX
// Runtime: `model.onnx` + `vocab.txt` in one folder. CLS pooling,
// L2-normalised, as the model card says; queries get its retrieval
// instruction. Runs on the VPS's ARM cores in tens of milliseconds.
// ------------------------------------------------------------------
public sealed class OnnxEmbedder : IEmbedder, IDisposable
{
    public const string QueryInstruction = "Represent this sentence for searching relevant passages: ";
    private const int MaxTokens = 512;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly bool _hasTokenTypes;
    private readonly object _lock = new object();

    public string ModelId { get; }

    public OnnxEmbedder(string folder)
    {
        string model = Path.Combine(folder, "model.onnx");
        string vocab = Path.Combine(folder, "vocab.txt");
        SessionOptions opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(model, opts);
        _tokenizer = BertTokenizer.Create(vocab, new BertOptions { LowerCaseBeforeTokenization = true });
        _hasTokenTypes = _session.InputMetadata.ContainsKey("token_type_ids");
        ModelId = "bge-small-en-v1.5:" + new FileInfo(model).Length;
    }

    /// Null when the folder has no model (the bot then searches by keyword
    /// only and says so in its log).
    public static OnnxEmbedder TryLoad(string folder, Action<string> log)
    {
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "model.onnx")) || !File.Exists(Path.Combine(folder, "vocab.txt")))
        {
            log("Embeddings: no model.onnx + vocab.txt in '" + folder + "' - keyword search only");
            return null;
        }
        try { return new OnnxEmbedder(folder); }
        catch (Exception e) { log("Embeddings: the model did not load (" + e.Message + ") - keyword search only"); return null; }
    }

    public float[] EmbedDocument(string text) => Embed(text);
    public float[] EmbedQuery(string text) => Embed(QueryInstruction + text);

    private float[] Embed(string text)
    {
        // [CLS] ... [SEP], cut to the model's 512 with the [SEP] kept.
        List<int> ids = new List<int>(_tokenizer.EncodeToIds(text ?? ""));
        if (ids.Count > MaxTokens)
        {
            ids.RemoveRange(MaxTokens - 1, ids.Count - (MaxTokens - 1));
            ids.Add(_tokenizer.SeparatorTokenId);
        }
        int n = ids.Count;
        DenseTensor<long> input = new DenseTensor<long>(new[] { 1, n });
        DenseTensor<long> mask = new DenseTensor<long>(new[] { 1, n });
        DenseTensor<long> types = new DenseTensor<long>(new[] { 1, n });
        for (int i = 0; i < n; i++) { input[0, i] = ids[i]; mask[0, i] = 1; }
        List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", input),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        };
        if (_hasTokenTypes) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", types));

        lock (_lock)
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs);
            Tensor<float> hidden = outputs.First().AsTensor<float>();   // [1, n, dim]
            int dim = hidden.Dimensions[2];
            float[] v = new float[dim];
            for (int d = 0; d < dim; d++) v[d] = hidden[0, 0, d];       // CLS
            return Vectors.Normalize(v);
        }
    }

    public void Dispose() => _session.Dispose();
}

public static class Vectors
{
    public static float[] Normalize(float[] v)
    {
        double sum = 0;
        foreach (float x in v) sum += x * x;
        float len = (float)Math.Sqrt(sum);
        if (len > 0) for (int i = 0; i < v.Length; i++) v[i] /= len;
        return v;
    }

    /// Dot product - the cosine for normalised vectors.
    public static float Dot(float[] a, float[] b)
    {
        float s = 0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) s += a[i] * b[i];
        return s;
    }

    public static byte[] ToBytes(float[] v)
    {
        byte[] b = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, b, 0, b.Length);
        return b;
    }

    public static float[] FromBytes(byte[] b)
    {
        float[] v = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, v, 0, v.Length * 4);
        return v;
    }
}
