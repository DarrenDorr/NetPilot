#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>

#include "packet_queue.h"

static int require_true(int condition, const char *message)
{
    if (condition)
        return 0;

    fprintf(stderr, "delayed_queue_zero_budget_harness: FAIL: %s\n", message);
    return 1;
}

int main(void)
{
    struct sockaddr_storage dst;
    uint8_t data[16];
    struct rawpacket_queue q;
    struct rawpacket *rp;

    memset(&dst, 0, sizeof(dst));
    memset(data, 0x5A, sizeof(data));

    rawpacket_queue_init(&q, 2);

    if (require_true(q.max_bytes == 0, "fresh queue must start with an unconfigured zero byte budget"))
        return 1;
    if (require_true(q.queued_bytes == 0, "fresh queue must start with zero accounted bytes"))
        return 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                     data, 1, 1, NULL, false) == NULL,
                     "zero/unconfigured byte budget must reject delayed packet state"))
        return 1;
    if (require_true(rawpacket_queue_count(&q) == 0 && q.queued_bytes == 0,
                     "zero-budget rejection must not mutate queue accounting"))
        return 1;

    rawpacket_queue_set_max_bytes(&q, 8);

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                     data, 4, 4, NULL, false) != NULL,
                     "configured byte budget must admit packet within budget"))
        return 1;
    if (require_true(q.queued_bytes == 4,
                     "configured enqueue must update byte accounting"))
        return 1;

    if (require_true(rawpacket_queue(&q, &dst, 0, 0, NULL, NULL,
                                     data, 5, 5, NULL, false) == NULL,
                     "configured byte budget must reject packet beyond remaining capacity"))
        return 1;
    if (require_true(q.queued_bytes == 4,
                     "rejected configured enqueue must preserve byte accounting"))
        return 1;

    rp = rawpacket_dequeue(&q);
    if (require_true(rp != NULL, "dequeue must return admitted packet"))
        return 1;
    rawpacket_free(rp);

    if (require_true(q.queued_bytes == 0 && rawpacket_queue_empty(&q),
                     "drain must restore empty zero-byte state"))
        return 1;

    rawpacket_queue_destroy(&q);
    puts("delayed_queue_zero_budget_harness: PASS");
    return 0;
}
