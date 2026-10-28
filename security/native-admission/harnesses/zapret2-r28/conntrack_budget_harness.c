#include <arpa/inet.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "params.h"

struct params_s params;

/*
 * Isolated harness stubs. The test exercises the real patched conntrack.c,
 * but does not need a Lua VM, packet transmission or TCP option parsing.
 */
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
    fprintf(stderr, "conntrack_budget_harness: FAIL: %s\n", message);
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

static void make_udp_fixture(struct udp_fixture *fixture,
                             uint32_t src_host,
                             uint32_t dst_host,
                             uint16_t sport_host,
                             uint16_t dport_host)
{
    memset(fixture, 0, sizeof(*fixture));

    fixture->ip.ip_v = 4;
    fixture->ip.ip_hl = 5;
    fixture->ip.ip_p = IPPROTO_UDP;
    fixture->ip.ip_src.s_addr = htonl(src_host);
    fixture->ip.ip_dst.s_addr = htonl(dst_host);

    fixture->udp.uh_sport = htons(sport_host);
    fixture->udp.uh_dport = htons(dport_host);
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

int main(void)
{
    t_conntrack pool;
    t_ctrack *track = NULL;
    bool reverse = false;
    struct udp_fixture first;
    struct udp_fixture first_reverse;
    struct udp_fixture second;
    struct udp_fixture third;
    struct udp_fixture fourth;
    struct dissect invalid;

    memset(&params, 0, sizeof(params));
    memset(&invalid, 0, sizeof(invalid));

    make_udp_fixture(&first,         0x0a000001u, 0x0a000002u, 10001, 443);
    make_udp_fixture(&first_reverse, 0x0a000002u, 0x0a000001u, 443, 10001);
    make_udp_fixture(&second,        0x0a000003u, 0x0a000004u, 10002, 443);
    make_udp_fixture(&third,         0x0a000005u, 0x0a000006u, 10003, 443);
    make_udp_fixture(&fourth,        0x0a000007u, 0x0a000008u, 10004, 443);

    ConntrackPoolInit(&pool, 60, 60, 300, 60, 60);
    ConntrackPoolSetMaxEntries(&pool, 3);

    require_true(pool_count(&pool) == 0, "pool must start empty");
    require_true(ConntrackPoolFeed(&pool, &invalid, &track, &reverse) == CTRACK_FEED_NONE,
                 "non-IP/non-transport input must not create state");
    require_true(pool_count(&pool) == 0, "invalid input must leave pool empty");

    require_true(ConntrackPoolFeed(&pool, &first.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "first UDP flow must be admitted");
    require_true(track != NULL && !reverse, "first flow must return a forward track");
    require_true(pool_count(&pool) == 1, "pool count must become one");

    track = NULL;
    reverse = false;
    require_true(ConntrackPoolFeed(&pool, &second.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "second UDP flow must be admitted");
    require_true(ConntrackPoolFeed(&pool, &third.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "third UDP flow must be admitted");
    require_true(pool_count(&pool) == 3, "pool must reach configured max_entries");

    /* Existing flow updates must continue to work at saturation. */
    track = NULL;
    reverse = true;
    require_true(ConntrackPoolFeed(&pool, &first.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "existing flow must remain usable at saturation");
    require_true(track != NULL && !reverse, "existing forward flow direction changed");
    require_true(pool_count(&pool) == 3, "existing flow must not allocate a new entry");

    /* Reverse lookup of an existing flow must also remain usable. */
    track = NULL;
    reverse = false;
    require_true(ConntrackPoolFeed(&pool, &first_reverse.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "reverse packet of existing flow must remain usable at saturation");
    require_true(track != NULL && reverse, "reverse flow must be identified as reverse");
    require_true(pool_count(&pool) == 3, "reverse flow must not allocate a new entry");

    track = NULL;
    reverse = false;
    require_true(ConntrackPoolFeed(&pool, &fourth.dis, &track, &reverse) == CTRACK_FEED_RESOURCE_EXHAUSTED,
                 "new flow beyond max_entries must report RESOURCE_EXHAUSTED");
    require_true(track == NULL, "rejected flow must not expose a track");
    require_true(pool_count(&pool) == 3, "rejected flow must not grow the pool");

    /* max_entries == 0 intentionally means unlimited for the internal primitive. */
    ConntrackPoolSetMaxEntries(&pool, 0);
    require_true(ConntrackPoolFeed(&pool, &fourth.dis, &track, &reverse) == CTRACK_FEED_OK,
                 "unlimited internal mode must accept the fourth flow");
    require_true(pool_count(&pool) == 4, "pool must contain four flows after removing the cap");

    ConntrackPoolDestroy(&pool);
    require_true(pool_count(&pool) == 0, "destroy must free all conntrack entries");

    puts("conntrack_budget_harness: PASS");
    return 0;
}
