'use client'

import { useRouter } from 'next/navigation'
import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
} from '@/components/ui/pagination'
import type { CustomerPagedResult, CustomerQueryFilters } from '@/models'

interface Props {
  customers: CustomerPagedResult
  currentFilters: CustomerQueryFilters
}

export const CustomersPagination = ({ customers, currentFilters }: Props) => {
  const router = useRouter()

  const buildHref = (cursor: string) => {
    const params = new URLSearchParams({ limit: currentFilters.limit })
    if (cursor) params.set('cursor', cursor)
    return `/customers?${params.toString()}`
  }

  const canGoPrevious = customers.hasPrevious && !!customers.previousCursor
  const canGoNext = customers.hasMore && !!customers.nextCursor

  const goTo = (href: string, allowed: boolean) => {
    if (!allowed) return
    router.push(href)
  }

  if (!canGoPrevious && !canGoNext) return null

  return (
    <div className="flex flex-col items-center gap-2">
      <Pagination>
        <PaginationContent>
          <PaginationItem>
            <PaginationPrevious
              href={buildHref(customers.previousCursor)}
              onClick={(e) => {
                e.preventDefault()
                goTo(buildHref(customers.previousCursor), canGoPrevious)
              }}
              aria-disabled={!canGoPrevious}
              className={!canGoPrevious ? 'pointer-events-none opacity-50' : ''}
            />
          </PaginationItem>

          <PaginationItem>
            <PaginationNext
              href={buildHref(customers.nextCursor)}
              onClick={(e) => {
                e.preventDefault()
                goTo(buildHref(customers.nextCursor), canGoNext)
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
