'use client'

import { useRouter } from 'next/navigation'
import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
} from '@/components/ui/pagination'
import type { ProductPagedResult, ProductQueryFilters } from '@/models'

interface Props {
  products: ProductPagedResult
  currentFilters: ProductQueryFilters
}

/**
 * Keyset (cursor) navigation: Previous uses previousCursor + pageDirection=Backward,
 * Next uses nextCursor + pageDirection=Forward. No page numbers — the API is cursor-based.
 */
export const ProductsPagination = ({ products, currentFilters }: Props) => {
  const router = useRouter()

  const buildHref = (cursor: string, pageDirection: 'Forward' | 'Backward') => {
    const params = new URLSearchParams({
      limit: currentFilters.limit,
      sortBy: currentFilters.sortBy,
      sortDirection: currentFilters.sortDirection,
      pageDirection,
    })
    if (cursor) params.set('cursor', cursor)
    return `/products?${params.toString()}`
  }

  const canGoPrevious = products.hasPrevious && !!products.previousCursor
  const canGoNext = products.hasMore && !!products.nextCursor

  const goTo = (href: string, allowed: boolean) => {
    if (!allowed) return
    router.push(href)
  }

  if (!canGoPrevious && !canGoNext) return null

  return (
    <div className="flex flex-col items-center gap-2">
      {products.totalCount > 0 && (
        <p className="text-sm text-muted-foreground">
          Showing {products.items.length} of {products.totalCount} products
        </p>
      )}
      <Pagination>
        <PaginationContent>
          <PaginationItem>
            <PaginationPrevious
              href={buildHref(products.previousCursor, 'Backward')}
              onClick={(e) => {
                e.preventDefault()
                goTo(buildHref(products.previousCursor, 'Backward'), canGoPrevious)
              }}
              aria-disabled={!canGoPrevious}
              className={!canGoPrevious ? 'pointer-events-none opacity-50' : ''}
            />
          </PaginationItem>

          <PaginationItem>
            <PaginationNext
              href={buildHref(products.nextCursor, 'Forward')}
              onClick={(e) => {
                e.preventDefault()
                goTo(buildHref(products.nextCursor, 'Forward'), canGoNext)
              }}
              aria-disabled={!canGoNext}
              className={!canGoNext ? 'pointer-events-none opacity-50' : ''}
            />
          </PaginationItem>
        </PaginationContent>
      </Pagination>
    </div>
  )
}
