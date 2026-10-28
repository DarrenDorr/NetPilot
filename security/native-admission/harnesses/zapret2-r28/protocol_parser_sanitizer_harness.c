#include "protocol.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void fail(const char *message)
{
    fprintf(stderr, "protocol_parser_sanitizer_harness: FAIL: %s\n", message);
    exit(1);
}

static uint32_t prng_state = 0x4e505232u;

static uint32_t next_u32(void)
{
    uint32_t x = prng_state;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    prng_state = x;
    return x;
}

static void validate_position(ssize_t pos, size_t len)
{
    if (pos != POS_NOT_FOUND && (pos < 0 || (size_t)pos >= len))
        fail("parser returned an out-of-range marker position");
}

static void validate_span(const uint8_t *base, size_t len,
    const uint8_t *ptr, size_t span_len, const char *message)
{
    if (ptr == NULL)
        return;

    uintptr_t base_addr = (uintptr_t)base;
    uintptr_t ptr_addr = (uintptr_t)ptr;
    uintptr_t end_addr = base_addr + len;

    if (ptr_addr < base_addr || ptr_addr > end_addr)
        fail(message);
    if (span_len > (size_t)(end_addr - ptr_addr))
        fail(message);
}

static void exercise_tls(const uint8_t *data, size_t len)
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
    if (off > len)
        fail("TLSFindExtLen returned an offset outside the input");

    (void)TLSFindExt(data, len, 0, &ext, &ext_len, false);
    validate_span(data, len, ext, ext_len,
        "TLSFindExt returned an out-of-range extension");

    ext = NULL;
    ext_len = 0;
    (void)TLSFindExt(data, len, 0, &ext, &ext_len, true);
    validate_span(data, len, ext, ext_len,
        "partial TLSFindExt returned an out-of-range extension");

    memset(host, 0xA5, sizeof(host));
    (void)TLSHelloExtractHost(data, len, host, sizeof(host), false);
    host[sizeof(host) - 1] = 0;

    memset(host, 0xA5, sizeof(host));
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

static void exercise_quic(const uint8_t *data, size_t len)
{
    size_t hello_offset = 0;
    size_t hello_len = 0;
    quic_cid_t cid;

    (void)QUICIsLongHeader(data, len);
    (void)QUICExtractVersion(data, len);
    (void)IsQUICInitial(data, len);
    (void)IsQUICCryptoHello(data, len, &hello_offset, &hello_len);
    if (hello_offset > len || hello_len > len - hello_offset)
        fail("IsQUICCryptoHello returned an out-of-range span");

    memset(&cid, 0, sizeof(cid));
    if (QUICExtractDCID(data, len, &cid) && cid.len > QUIC_MAX_CID_LENGTH)
        fail("QUICExtractDCID returned an oversized CID");
}

static void exercise_other_protocols(const uint8_t *data, size_t len)
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

static void exercise_all(const uint8_t *data, size_t len)
{
    exercise_tls(data, len);
    exercise_quic(data, len);
    exercise_other_protocols(data, len);
}

static void test_valid_tls_and_all_truncations(void)
{
    static const uint8_t tls_client_hello[] = {
        0x16,0x03,0x01,0x00,0x43,
        0x01,0x00,0x00,0x3f,0x03,0x03,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,
        0x00,0x02,0x13,0x01,
        0x01,0x00,
        0x00,0x14,
        0x00,0x00,0x00,0x10,
        0x00,0x0e,0x00,0x00,0x0b,
        'e','x','a','m','p','l','e','.','c','o','m'
    };
    char host[256];

    if (sizeof(tls_client_hello) != 72)
        fail("known-good TLS ClientHello fixture length is inconsistent");
    if (!IsTLSClientHello(tls_client_hello, sizeof(tls_client_hello), false))
        fail("known-good TLS ClientHello was not recognized");

    memset(host, 0, sizeof(host));
    if (!TLSHelloExtractHost(tls_client_hello, sizeof(tls_client_hello), host, sizeof(host), false))
        fail("known-good TLS SNI was not extracted");
    if (strcmp(host, "example.com") != 0)
        fail("known-good TLS SNI changed unexpectedly");

    for (size_t len = 0; len <= sizeof(tls_client_hello); ++len)
        exercise_all(tls_client_hello, len);
}

static void test_short_input_patterns(void)
{
    uint8_t buffer[96];

    for (size_t len = 0; len <= sizeof(buffer); ++len)
    {
        memset(buffer, 0x00, sizeof(buffer));
        exercise_all(buffer, len);

        memset(buffer, 0xff, sizeof(buffer));
        exercise_all(buffer, len);

        for (size_t i = 0; i < sizeof(buffer); ++i)
            buffer[i] = (uint8_t)i;
        exercise_all(buffer, len);
    }
}

static void test_deterministic_random_inputs(void)
{
    uint8_t buffer[2048];

    for (size_t iteration = 0; iteration < 12000; ++iteration)
    {
        size_t len = (size_t)(next_u32() % (sizeof(buffer) + 1));
        for (size_t i = 0; i < len; ++i)
            buffer[i] = (uint8_t)next_u32();

        if (len > 0 && (iteration % 4) == 0)
            buffer[0] = 0x16;
        if (len > 5 && (iteration % 5) == 0)
        {
            buffer[0] = 0x16;
            buffer[1] = 0x03;
            buffer[2] = (uint8_t)(next_u32() & 0x03u);
            buffer[5] = (iteration & 1u) ? 0x01 : 0x02;
        }
        if (len > 0 && (iteration % 7) == 0)
            buffer[0] |= 0xC0u;

        exercise_all(buffer, len);
    }
}

int main(void)
{
    static const uint8_t empty = 0;

    exercise_all(&empty, 0);
    test_valid_tls_and_all_truncations();
    test_short_input_patterns();
    test_deterministic_random_inputs();

    puts("protocol_parser_sanitizer_harness: PASS");
    return 0;
}
