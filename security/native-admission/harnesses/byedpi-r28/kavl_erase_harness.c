#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "kavl.h"

struct test_node {
    int key;
    KAVL_HEAD(struct test_node) head;
};

#define test_cmp(p, q) (((q)->key < (p)->key) - ((p)->key < (q)->key))
KAVL_INIT(test, struct test_node, head, test_cmp)

static void fail(const char *message)
{
    fprintf(stderr, "kavl_erase_harness: FAIL: %s\n", message);
    exit(1);
}

static void require_true(int condition, const char *message)
{
    if (!condition)
        fail(message);
}

static uint32_t prng_state = 0x4e504552u;

static uint32_t next_u32(void)
{
    uint32_t x = prng_state;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    prng_state = x;
    return x;
}

static struct test_node *make_node(int key)
{
    struct test_node *node = calloc(1, sizeof(*node));
    if (!node)
        fail("allocation failed");
    node->key = key;
    return node;
}

static void shuffle(size_t *order, size_t count)
{
    for (size_t i = 0; i < count; ++i)
        order[i] = i;

    for (size_t i = count; i > 1; --i) {
        size_t j = (size_t)(next_u32() % (uint32_t)i);
        size_t tmp = order[i - 1];
        order[i - 1] = order[j];
        order[j] = tmp;
    }
}

static struct test_node *build_tree(size_t count)
{
    size_t *order = malloc(count * sizeof(*order));
    struct test_node *root = NULL;
    if (!order)
        fail("order allocation failed");

    shuffle(order, count);
    for (size_t i = 0; i < count; ++i) {
        struct test_node *node = make_node((int)(order[i] * 2u));
        struct test_node *inserted = kavl_insert(test, &root, node, NULL);
        require_true(inserted == node, "unique node was treated as duplicate");
    }

    free(order);
    return root;
}

static void erase_exact(size_t count)
{
    struct test_node *root = build_tree(count);
    size_t *order = malloc(count * sizeof(*order));
    if (!order)
        fail("delete order allocation failed");

    shuffle(order, count);
    for (size_t i = 0; i < count; ++i) {
        struct test_node query;
        memset(&query, 0, sizeof(query));
        query.key = (int)(order[i] * 2u);

        struct test_node *removed = kavl_erase(test, &root, &query, NULL);
        require_true(removed != NULL, "exact erase failed to find existing key");
        require_true(removed->key == query.key, "exact erase returned wrong key");
        free(removed);
    }

    require_true(root == NULL, "exact erase did not empty tree");
    free(order);
}

static void erase_first(size_t count)
{
    struct test_node *root = build_tree(count);
    int previous = -1;
    size_t removed_count = 0;

    while (root != NULL) {
        struct test_node *removed = kavl_erase_first(test, &root);
        require_true(removed != NULL, "erase_first returned NULL for non-empty tree");
        require_true(removed->key > previous, "erase_first order was not strictly ascending");
        previous = removed->key;
        ++removed_count;
        free(removed);
    }

    require_true(removed_count == count, "erase_first removed unexpected node count");
}

int main(int argc, char **argv)
{
    enum { NODE_COUNT = 1024, CYCLES = 20 };

    if (argc != 2)
        fail("expected mode: exact or first");

    for (size_t cycle = 0; cycle < CYCLES; ++cycle) {
        if (strcmp(argv[1], "exact") == 0)
            erase_exact(NODE_COUNT);
        else if (strcmp(argv[1], "first") == 0)
            erase_first(NODE_COUNT);
        else
            fail("unknown mode");
    }

    printf("kavl_erase_harness: PASS mode=%s\n", argv[1]);
    return 0;
}
