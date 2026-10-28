#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>

#include "packet_queue.h"

static int require_true(int condition, const char *message)
{
    if (condition)
        return 0;

    fprintf(stderr, "FAIL: %s\n", message);
    return 1;
}

int main(void)
{
    struct sockaddr_storage dst;
    uint8_t data[16];
    struct rawpacket_queue q;
    struct rawpacket *rp;

    memset(&dst, 0, sizeof(dst));
    memset(data, 0xA5, sizeof(data));

    rawpacket_queue_init(&q, 2);
    rawpacket_queue_set_max_bytes(&q, 8);

    if (require_true(q.queued_bytes == 0, "initial queued_bytes must be zero"))
        return 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL, data, 4, 4, NULL, false) != NULL,
                     "first 4-byte packet must fit"))
        return 1;
    if (require_true(q.queued_bytes == 4, "queued_bytes must become 4"))
        return 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL, data, 5, 5, NULL, false) == NULL,
                     "byte budget must reject 4+5 > 8"))
        return 1;
    if (require_true(q.queued_bytes == 4, "failed enqueue must not change queued_bytes"))
        return 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL, data, 4, 4, NULL, false) != NULL,
                     "second 4-byte packet must fit exactly"))
        return 1;
    if (require_true(q.queued_bytes == 8, "queued_bytes must become 8"))
        return 1;
    if (require_true(rawpacket_queue_count(&q) == 2, "queue must contain two packets"))
        return 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL, data, 1, 1, NULL, false) == NULL,
                     "existing max_packets bound must still reject packet 3"))
        return 1;

    rp = rawpacket_dequeue(&q);
    if (require_true(rp != NULL, "dequeue must return the first packet"))
        return 1;
    if (require_true(q.queued_bytes == 4, "dequeue must subtract packet bytes"))
        return 1;
    rawpacket_free(rp);

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL, data, 4, 4, NULL, false) != NULL,
                     "budget must be reusable after dequeue"))
        return 1;
    if (require_true(q.queued_bytes == 8, "queued_bytes must return to 8"))
        return 1;

    rawpacket_queue_destroy(&q);
    if (require_true(rawpacket_queue_empty(&q), "destroy must empty the queue"))
        return 1;
    if (require_true(q.queued_bytes == 0, "destroy must reset queued_bytes"))
        return 1;

    /* Exercise the overflow guard without allocating an enormous buffer. */
    rawpacket_queue_init(&q, 64);
    rawpacket_queue_set_max_bytes(&q, SIZE_MAX);
    q.queued_bytes = SIZE_MAX - 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL, data, 2, 2, NULL, false) == NULL,
                     "SIZE_MAX overflow must be rejected before allocation/copy"))
        return 1;
    if (require_true(rawpacket_queue_count(&q) == 0, "overflow rejection must not enqueue data"))
        return 1;

    q.queued_bytes = 0;
    rawpacket_queue_destroy(&q);

    puts("packet_queue_budget_harness: PASS");
    return 0;
}
