using Unity.Netcode;
using Overrun.Core;

namespace Overrun.Net
{
    /// <summary>
    /// Transport adapter for <see cref="InputFrame"/>.
    ///
    /// InputFrame itself lives in Overrun.Core with no Netcode dependency (see the
    /// remarks on that type). Since NGO 1.0.0-pre.8, bare unmanaged structs are no
    /// longer accepted as RPC parameters — they must be tagged INetworkSerializeByMemcpy,
    /// or wrapped in ForceNetworkSerializeByMemcpy&lt;T&gt; when the owning assembly
    /// cannot take the dependency. That is exactly our case.
    ///
    /// Usage in an RPC signature:
    ///     [Rpc(SendTo.Server)]
    ///     void SubmitInputRpc(NetInputFrame frame, RpcParams rpc = default)
    /// </summary>
    public struct NetInputFrame : INetworkSerializeByMemcpy
    {
        public InputFrame Frame;

        public NetInputFrame(InputFrame frame) => Frame = frame;

        public static implicit operator NetInputFrame(InputFrame frame) => new NetInputFrame(frame);
        public static implicit operator InputFrame(NetInputFrame wrapper) => wrapper.Frame;
    }
}
