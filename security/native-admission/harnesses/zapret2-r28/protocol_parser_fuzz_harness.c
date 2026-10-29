#include "protocol.h"

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

static void abort_if(bool condition)
{
    if (condition)
        abort();
}

static void validate_position(ssize_t pos, size_t len)
{
    abort_if(pos != POS_NOT_FOUND && (pos < 0 || (size_t)pos >= len));
}

static void validate_span(const uint8_t *base, size_t len,
                          const uint8_t *ptr, size_t span_len)
{
    if (ptr == NULL)
        return;

    uintptr_t base_addr = (uintptr_t)base;
    uintptr_t ptr_addr = (uintptr_t)ptr;
    uintptr_t end_addr = base_addr + len;

    abort_if(ptr_addr < base_addr || ptr_addr > end_addr);
    abort_if(span_len > (size_t)(end_addr - ptr_addr));
}

static void fuzz_tls(const uint8_t *data, size_t len)
{
    const uint8_t *ext = NULL;
    size_t ext_len = 0;
    size_t off = 0;
    char host[256];

    (void)IsTLSClientHello(data, len, false);
    (void)IsTLSClientHello(data, len, true);
    (void)IsTLSServerHello(data, len, false);
    (void)IsTLSServerHello(data, len, true);

    (void)TLSFindExtLen(data, len, &off);
    abort_if(off > len);

    (void)TLSFindExt(data, len, 0, &ext, &ext_len, false);
    validate_span(data, len, ext, ext_len);

    ext = NULL;
    ext_len = 0;
    (void)TLSFindExt(data, len, 0, &ext, &ext_len, true);
    validate_span(data, len, ext, ext_len);

    memset(host, 0, sizeof(host));
    (void)TLSHelloExtractHost(data, len, host, sizeof(host), false);
    host[sizeof(host) - 1] = 0;

    memset(host, 0, sizeof(host));
    (void)TLSHelloExtractHost(data, len, host, sizeof(host), true);
    host[sizeof(host) - 1] = 0;

    validate_position(TLSPos(PM_HOST, 0, data, len), len);
    validate_position(TLSPos(PM_HOST_END, 0, data, len), len);
    validate_position(TLSPos(PM_HOST_SLD, 0, data, len), len);
    validate_position(TLSPos(PM_HOST_MIDSLD, 0, data, len), len);
    validate_position(TLSPos(PM_HOST_ENDSLD, 0, data, len), len);
    validate_position(TLSPos(PM_EXT_LEN, 0, data, len), len);
    validate_position(TLSPos(PM_SNI_EXT, 0, data, len), len);
}

static void fuzz_quic(const uint8_t *data, size_t len)
{
    size_t hello_offset = 0;
    size_t hello_len = 0;
    quic_cid_t cid;

    (void)QUICIsLongHeader(data, len);
    (void)QUICExtractVersion(data, len);
    (void)IsQUICInitial(data, len);
    (void)IsQUICCryptoHello(data, len, &hello_offset, &hello_len);

    abort_if(hello_offset > len);
    abort_if(hello_len > len - hello_offset);

    memset(&cid, 0, sizeof(cid));
    if (QUICExtractDCID(data, len, &cid))
        abort_if(cid.len > QUIC_MAX_CID_LENGTH);
}

static void fuzz_other_protocols(const uint8_t *data, size_t len)
{
    char host[256];

    (void)IsHttp(data, len);
    memset(host, 0, sizeof(host));
    (void)HttpExtractHost(data, len, host, sizeof(host));
    (void)IsHttpReply(data, len);
    (void)IsDNSQuery(data, len);
    (void)IsDNSResponse(data, len);
    (void)IsDiscordIpDiscoveryRequest(data, len);
    (void)IsStunMessage(data, len);
    (void)IsDTLS(data, len);
    (void)IsDTLSClientHello(data, len);
    (void)IsDTLSServerHello(data, len);
    (void)IsWireguardHandshakeInitiation(data, len);
    (void)IsWireguardHandshakeResponse(data, len);
    (void)IsWireguardHandshakeCookie(data, len);
    (void)IsWireguardKeepalive(data, len);
    (void)IsWireguardData(data, len);
    (void)IsDht(data, len);
    (void)IsMTProto(data, len);
    (void)IsBTHandshake(data, len);
    (void)IsUTP_BTHandshake(data, len);
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t len)
{
    static const uint8_t empty = 0;

    if (data == NULL) {
        data = &empty;
        len = 0;
    }

    fuzz_tls(data, len);
    fuzz_quic(data, len);
    fuzz_other_protocols(data, len);
    return 0;
}
