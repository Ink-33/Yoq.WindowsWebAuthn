﻿using Yoq.WindowsWebAuthn.Pinvoke;
using F2 = Fido2NetLib;

namespace Yoq.WindowsWebAuthn.Managed
{
    public enum WebAuthnResult : uint
    {
        Success = 0,
        UserCancelled,
        CancellationRequested,
        CredentialExists,
        TimeoutExpired,
        PlatformAuthUnavailable,
        ConstraintNotSatisfied,
        NotSupported,
        TokenStorageFull,
        NotAllowed
        // enum contains original HRESULT if an unknown error is returned
    }

    public static class WebAuthn
    {
        private static bool? _available;
        public static bool ApiAvailable => _available ?? (_available = WebAuthnApi.CheckApiAvailable()).Value;
        public static int ApiVersion => WebAuthnApi.ApiVersion;

        private static bool? _userVerifyingPlatformAuthenticatorAvailable;
        public static bool UserVerifyingPlatformAuthenticatorAvailable
        {
            get
            {
                if (_userVerifyingPlatformAuthenticatorAvailable.HasValue) return _userVerifyingPlatformAuthenticatorAvailable.Value;
                if (WebAuthnApi.IsUserVerifyingPlatformAuthenticatorAvailable(out var avl) == WebAuthnHResult.Ok)
                    _userVerifyingPlatformAuthenticatorAvailable = avl;
                else
                    _userVerifyingPlatformAuthenticatorAvailable = false;
                return _userVerifyingPlatformAuthenticatorAvailable.Value;
            }
        }

        private static bool CheckFailure(WebAuthnHResult hresult, CancellationToken? ct, out WebAuthnResult result)
        {
            result = WebAuthnResult.Success;
            if (hresult == WebAuthnHResult.Ok) return false;
            result = hresult switch
            {
                WebAuthnHResult.NteExists => WebAuthnResult.CredentialExists,
                WebAuthnHResult.Timeout => WebAuthnResult.TimeoutExpired,
                WebAuthnHResult.Canceled when ct?.IsCancellationRequested ?? false => WebAuthnResult.CancellationRequested,
                WebAuthnHResult.Canceled => WebAuthnResult.UserCancelled,
                WebAuthnHResult.NteUserCanceled => WebAuthnResult.UserCancelled,
                WebAuthnHResult.NteBadKeyset => WebAuthnResult.PlatformAuthUnavailable,
                WebAuthnHResult.NteInvalidParameter => WebAuthnResult.NotSupported,
                WebAuthnHResult.NteTokenKeysetStorageFull => WebAuthnResult.TokenStorageFull,
                WebAuthnHResult.NotSupported => WebAuthnResult.ConstraintNotSatisfied,
                WebAuthnHResult.NteNotSupported => WebAuthnResult.ConstraintNotSatisfied,
                WebAuthnHResult.NteDeviceNotFound => WebAuthnResult.NotAllowed,
                WebAuthnHResult.NteNotFound => WebAuthnResult.NotAllowed,
                _ => (WebAuthnResult)hresult
            };
            return true;
        }

        public static WebAuthnResult MakeCredential(IntPtr hwnd, F2.CredentialCreateOptions opts, string origin, out F2.AuthenticatorAttestationRawResponse response, CancellationToken? ct = null)
        {
#nullable disable
            response = null;
#nullable restore

            Guid? cancelGuid = null;
            CancellationTokenRegistration? cancelReg = null;
            if (ct.HasValue)
            {
                if (WebAuthnApi.GetCancellationId(out var cg) != WebAuthnHResult.Ok) return WebAuthnResult.NotSupported;
                cancelGuid = cg;
                cancelReg = ct.Value.Register(() => WebAuthnApi.CancelCurrentOperation(cg));
            }

            var clientData = opts.ToClientData(origin);
            var res = WebAuthnApi.AuthenticatorMakeCredential(hwnd, opts.ToRelayingPartyInfo(), opts.ToUserInfo(),
                                                                    opts.ToCoseParamsList(), clientData,
                                                                    opts.ToAuthenticatorMakeCredentialOptions(cancelGuid),
                                                                    out var credential);
            cancelReg?.Dispose();
            if (CheckFailure(res, ct, out var result)) return result;

            response = new F2.AuthenticatorAttestationRawResponse
            {
                Id = credential.CredentialId,
                RawId = credential.CredentialId,
                Response = new F2.AuthenticatorAttestationRawResponse.ResponseData
                {
                    AttestationObject = credential.AttestationObject,
                    ClientDataJson = clientData.ClientDataJSON
                },
                Type = F2.Objects.PublicKeyCredentialType.PublicKey
            };

            return WebAuthnResult.Success;
        }

        public static WebAuthnResult GetAssertion(IntPtr hwnd, F2.AssertionOptions opts, string origin, out F2.AuthenticatorAssertionRawResponse response, CancellationToken? ct = null)
        {
#nullable disable
            response = null;
#nullable restore

            Guid? cancelGuid = null;
            CancellationTokenRegistration? cancelReg = null;
            if (ct.HasValue)
            {
                if (WebAuthnApi.GetCancellationId(out var cg) != WebAuthnHResult.Ok) return WebAuthnResult.NotSupported;
                cancelGuid = cg;
                cancelReg = ct.Value.Register(() => WebAuthnApi.CancelCurrentOperation(cg));
            }

            var clientData = opts.ToClientData(origin);
            var res = WebAuthnApi.AuthenticatorGetAssertion(hwnd, opts.RpId, clientData, opts.ToAssertionOptions(cancelGuid), out var assertion);
            cancelReg?.Dispose();
            if (CheckFailure(res, ct, out var result)) return result;

            response = new F2.AuthenticatorAssertionRawResponse()
            {
                Id = assertion.Credential.CredentialId,
                RawId = assertion.Credential.CredentialId,
                Type = F2.Objects.PublicKeyCredentialType.PublicKey,
                Response = new F2.AuthenticatorAssertionRawResponse.AssertionResponse()
                {
                    AuthenticatorData = assertion.AuthenticatorData,
                    Signature = assertion.Signature,
                    UserHandle = assertion.UserId,
                    ClientDataJson = clientData.ClientDataJSON
                },
            };

            return WebAuthnResult.Success;
        }

        public static Task<(WebAuthnResult, F2.AuthenticatorAttestationRawResponse)> MakeCredentialAsync(IntPtr hwnd, F2.CredentialCreateOptions opts, string origin, CancellationToken? ct = null)
            => Task.Run(() => { var res = MakeCredential(hwnd, opts, origin, out var resp, ct); return (res, resp); });

        public static Task<(WebAuthnResult, F2.AuthenticatorAssertionRawResponse)> GetAssertionAsync(IntPtr hwnd, F2.AssertionOptions opts, string origin, CancellationToken? ct = null)
            => Task.Run(() => { var res = GetAssertion(hwnd, opts, origin, out var resp, ct); return (res, resp); });
    }
}
