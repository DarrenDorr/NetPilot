#include <arpa/inet.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "params.h"
#include "packet_queue.h"

struct params_s params;

void lua_createtable(lua_State *L, int narr, int nrec)
{
    (void)L;
    (void)narr;
    (void)nrec;
}

int luaL_ref(lua_State *L, int t)
{
    static int next_ref = 1;
    (void)L;
    (void)t;
    return next_ref++;
}

void luaL_unref(lua_State *L, int t, int ref)
{
    (void)L;
    (void)t;
    (void)ref;
}

time_t boottime(void)
{
    return 1;
}

void extract_ports(const struct tcphdr *tcp, const struct udphdr *udp,
                   uint8_t *proto, uint16_t *sport, uint16_t *dport)
{
    if (udp != NULL) {
        *proto = IPPROTO_UDP;
        *sport = udp->uh_sport;
        *dport = udp->uh_dport;
    } else if (tcp != NULL) {
        *proto = IPPROTO_TCP;
        *sport = tcp->th_sport;
        *dport = tcp->th_dport;
    } else {
        *proto = IPPROTO_NONE;
        *sport = 0;
        *dport = 0;
    }
}

bool tcp_syn_segment(const struct tcphdr *tcp)
{
    return tcp != NULL && (tcp->th_flags & (TH_SYN | TH_ACK)) == TH_SYN;
}

bool tcp_synack_segment(const struct tcphdr *tcp)
{
    return tcp != NULL && (tcp->th_flags & (TH_SYN | TH_ACK)) == (TH_SYN | TH_ACK);
}

uint8_t tcp_find_scale_factor(const struct tcphdr *tcp)
{
    (void)tcp;
    return SCALE_NONE;
}

uint16_t tcp_find_mss(const struct tcphdr *tcp)
{
    (void)tcp;
    return 0;
}

static void fail(const char *message)
{
    fprintf(stderr, "resource_stress_harness: FAIL: %s\n", message);
    exit(1);
}

static void require_true(bool condition, const char *message)
{
    if (!condition)
        fail(message);
}

struct udp_fixture {
    struct ip ip;
    struct udphdr udp;
    struct dissect dis;
};

static void make_udp_fixture(struct udp_fixture *fixture, size_t index)
{
    const uint32_t src = 0x0a000001u + (uint32_t)(index * 2u);
    const uint32_t dst = 0x0b000001u + (uint32_t)(index * 2u);
    const uint16_t sport = (uint16_t)(10000u + (index % 50000u));

    memset(fixture, 0, sizeof(*fixture));
    fixture->ip.ip_v = 4;
    fixture->ip.ip_hl = 5;
    fixture->ip.ip_p = IPPROTO_UDP;
    fixture->ip.ip_src.s_addr = htonl(src);
    fixture->ip.ip_dst.s_addr = htonl(dst);
    fixture->udp.uh_sport = htons(sport);
    fixture->udp.uh_dport = htons(443);
    fixture->udp.uh_ulen = htons(sizeof(struct udphdr));
    fixture->dis.ip = &fixture->ip;
    fixture->dis.udp = &fixture->udp;
    fixture->dis.proto = IPPROTO_UDP;
    fixture->dis.len_l3 = sizeof(struct ip);
    fixture->dis.len_l4 = sizeof(struct udphdr);
    fixture->dis.transport_len = sizeof(struct udphdr);
    fixture->dis.len_payload = 0;
}

static size_t pool_count(const t_conntrack *pool)
{
    return (size_t)HASH_COUNT(pool->pool);
}

static void stress_conntrack_budget(size_t cap)
{
    t_conntrack pool;
    t_ctrack *track = NULL;
    bool reverse = false;
    struct udp_fixture fixture;
    struct udp_fixture first;

    ConntrackPoolInit(&pool, 60, 60, 300, 60, 60);
    ConntrackPoolSetMaxEntries(&pool, cap);
    make_udp_fixture(&first, 0);

    for (size_t i = 0; i < cap; ++i) {
        make_udp_fixture(&fixture, i);
        track = NULL;
        reverse = false;
        require_true(ConntrackPoolFeed(&pool, &fixture.dis, &track, &reverse) == CTRACK_FEED_OK,
                     "flow within configured conntrack budget was rejected");
        require_true(track != NULL && !reverse,
                     "admitted conntrack flow did not return forward state");
        require_true(pool_count(&pool) == i + 1,
                     "conntrack pool count drifted while filling to budget");
    }

    for (size_t i = 0; i < cap; ++i) {
        make_udp_fixture(&fixture, cap + i + 1);
        track = NULL;
        reverse = false;
        require_true(ConntrackPoolFeed(&pool, &fixture.dis, &track, &reverse) == CTRACK_FEED_RESOURCE_EXHAUSTED,
                     "flow beyond conntrack budget did not fail closed");
        require_true(track == NULL,
                     "rejected conntrack flow exposed state");
        require_true(pool_count(&pool) == cap,
                     "conntrack pool grew beyond configured budget");
    }

    track = NULL;
    reverse = true;
    require_true(ConntrackPoolFeed(&pool, &first.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "existing flow stopped working at conntrack saturation");
    require_true(track != NULL && !reverse,
                 "existing saturated flow changed direction");
    require_true(pool_count(&pool) == cap,
                 "existing saturated flow allocated new state");

    ConntrackPoolSetMaxEntries(&pool, 0);
    make_udp_fixture(&fixture, cap * 3u + 7u);
    track = NULL;
    reverse = false;
    require_true(ConntrackPoolFeed(&pool, &fixture.dis, &track, &reverse) == CTRACK_FEED_RESOURCE_EXHAUSTED,
                 "zero/unconfigured conntrack budget admitted new state");
    require_true(pool_count(&pool) == cap,
                 "zero/unconfigured conntrack budget changed pool size");

    ConntrackPoolDestroy(&pool);
    require_true(pool_count(&pool) == 0,
                 "conntrack destroy did not release all state");
}

static void drain_queue(struct rawpacket_queue *q)
{
    struct rawpacket *rp;
    while ((rp = rawpacket_dequeue(q)) != NULL)
        rawpacket_free(rp);
}

static void stress_packet_queue(void)
{
    struct sockaddr_storage dst;
    uint8_t packet[1500];
    struct rawpacket_queue q;

    memset(&dst, 0, sizeof(dst));
    memset(packet, 0x5a, sizeof(packet));

    for (size_t cycle = 0; cycle < 200; ++cycle) {
        rawpacket_queue_init(&q, 64);
        rawpacket_queue_set_max_bytes(&q, 64u * sizeof(packet));

        for (size_t i = 0; i < 64; ++i) {
            require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                         packet, sizeof(packet), sizeof(packet), NULL, false) != NULL,
                         "packet queue rejected entry within packet/byte budget");
        }

        require_true(rawpacket_queue_count(&q) == 64,
                     "packet queue count did not reach configured packet cap");
        require_true(q.queued_bytes == 64u * sizeof(packet),
                     "packet queue byte accounting drifted at saturation");
        require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                     packet, 1, 1, NULL, false) == NULL,
                     "packet queue admitted packet beyond saturation");
        require_true(q.queued_bytes == 64u * sizeof(packet),
                     "failed saturated enqueue changed byte accounting");

        drain_queue(&q);
        require_true(rawpacket_queue_empty(&q),
                     "packet queue did not become empty after drain");
        require_true(q.queued_bytes == 0,
                     "packet queue bytes did not return to zero after drain");
        rawpacket_queue_destroy(&q);
    }

    for (size_t cycle = 0; cycle < 500; ++cycle) {
        rawpacket_queue_init(&q, 64);
        rawpacket_queue_set_max_bytes(&q, 4096);

        for (size_t i = 0; i < 4; ++i) {
            require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                         packet, 1024, 1024, NULL, false) != NULL,
                         "byte-limited queue rejected entry within budget");
        }
        require_true(q.queued_bytes == 4096,
                     "byte-limited queue did not reach exact budget");
        require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                     packet, 1, 1, NULL, false) == NULL,
                     "byte-limited queue exceeded configured byte budget");

        drain_queue(&q);
        require_true(q.queued_bytes == 0 && rawpacket_queue_empty(&q),
                     "byte-limited queue did not fully recover after drain");
        rawpacket_queue_destroy(&q);
    }
}

int main(int argc, char **argv)
{
    static const size_t caps[] = {64, 512, 2048};

    (void)argc;
    (void)argv;
    memset(&params, 0, sizeof(params));

    for (size_t i = 0; i < sizeof(caps) / sizeof(caps[0]); ++i)
        stress_conntrack_budget(caps[i]);

    stress_packet_queue();

    puts("resource_stress_harness: PASS");
    return 0;
}
