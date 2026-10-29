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
    fprintf(stderr, "kavl_iterator_harness: FAIL: %s\n", message);
    exit(1);
}

static void require_true(int condition, const char *message)
{
    if (!condition)
        fail(message);
}

static uint32_t prng_state = 0x4e504b41u;

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

static void destroy_tree(struct test_node **root)
{
    struct test_node *node;
    while ((node = kavl_erase_first(test, root)) != NULL)
        free(node);
    require_true(*root == NULL, "tree root remained after destroy");
}

static void test_empty_iterator(void)
{
    struct test_node *root = NULL;
    struct test_node query;
    kavl_itr_t(test) itr;

    memset(&query, 0, sizeof(query));
    query.key = 10;

    kavl_itr_first(test, root, &itr);
    require_true(kavl_at(&itr) == NULL,
                 "empty first iterator returned a node");
    require_true(kavl_itr_next(test, &itr) == 0,
                 "empty iterator reported a next node");
    require_true(kavl_at(&itr) == NULL,
                 "empty iterator changed after next");

    require_true(kavl_itr_find(test, root, &query, &itr) == 0,
                 "empty find reported an exact match");
    require_true(kavl_at(&itr) == NULL,
                 "empty find returned a node");
}

static void build_shuffled_tree(struct test_node **root, size_t count)
{
    size_t *order = malloc(count * sizeof(*order));
    if (!order)
        fail("order allocation failed");

    for (size_t i = 0; i < count; ++i)
        order[i] = i;

    for (size_t i = count; i > 1; --i) {
        size_t j = (size_t)(next_u32() % (uint32_t)i);
        size_t tmp = order[i - 1];
        order[i - 1] = order[j];
        order[j] = tmp;
    }

    for (size_t i = 0; i < count; ++i) {
        int key = (int)(order[i] * 2u);
        struct test_node *node = make_node(key);
        struct test_node *inserted = kavl_insert(test, root, node, NULL);
        require_true(inserted == node, "unique node was treated as duplicate");
    }

    free(order);
}

static void test_in_order_iteration(struct test_node *root, size_t count)
{
    kavl_itr_t(test) itr;
    size_t seen = 0;

    kavl_itr_first(test, root, &itr);
    while (kavl_at(&itr) != NULL) {
        const struct test_node *node = kavl_at(&itr);
        require_true(node->key == (int)(seen * 2u),
                     "in-order iteration returned an unexpected key");
        ++seen;
        if (!kavl_itr_next(test, &itr))
            break;
    }

    require_true(seen == count, "in-order iteration count mismatch");
    require_true(kavl_itr_next(test, &itr) == 0,
                 "exhausted iterator reported another node");
    require_true(kavl_at(&itr) == NULL,
                 "exhausted iterator did not become empty");
}

static void test_find_semantics(struct test_node *root, size_t count)
{
    struct test_node query;
    kavl_itr_t(test) itr;

    memset(&query, 0, sizeof(query));

    for (size_t i = 0; i < count; ++i) {
        query.key = (int)(i * 2u);
        require_true(kavl_itr_find(test, root, &query, &itr) == 1,
                     "exact key was not found");
        require_true(kavl_at(&itr) != NULL && kavl_at(&itr)->key == query.key,
                     "exact find returned the wrong key");
    }

    for (size_t i = 0; i + 1 < count; ++i) {
        query.key = (int)(i * 2u + 1u);
        require_true(kavl_itr_find(test, root, &query, &itr) == 0,
                     "missing odd key was reported as exact match");
        require_true(kavl_at(&itr) != NULL,
                     "lower-bound find unexpectedly returned no node");
        require_true(kavl_at(&itr)->key == (int)((i + 1u) * 2u),
                     "lower-bound find returned the wrong successor");
    }

    query.key = -1;
    require_true(kavl_itr_find(test, root, &query, &itr) == 0,
                 "below-minimum query was reported as exact match");
    require_true(kavl_at(&itr) != NULL && kavl_at(&itr)->key == 0,
                 "below-minimum query did not return the first node");

    query.key = (int)(count * 2u + 1u);
    require_true(kavl_itr_find(test, root, &query, &itr) == 0,
                 "above-maximum query was reported as exact match");
    require_true(kavl_at(&itr) == NULL,
                 "above-maximum query should not have a lower-bound node");
}

static void test_repeated_cycles(void)
{
    enum { NODE_COUNT = 1024, CYCLES = 40 };

    for (size_t cycle = 0; cycle < CYCLES; ++cycle) {
        struct test_node *root = NULL;
        build_shuffled_tree(&root, NODE_COUNT);
        require_true(root != NULL, "tree was not created");
        test_in_order_iteration(root, NODE_COUNT);
        test_find_semantics(root, NODE_COUNT);
        destroy_tree(&root);
        test_empty_iterator();
    }
}

int main(void)
{
    test_empty_iterator();
    test_repeated_cycles();
    puts("kavl_iterator_harness: PASS");
    return 0;
}
