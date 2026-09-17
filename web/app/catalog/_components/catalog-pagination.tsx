'use client'

import { useRouter } from 'next/navigation'
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from '@/components/ui/pagination'

interface Filters {
  page: string
  pageSize: string
  sortBy: string
  sortDirection: string
}

interface Props {
  currentFilters: Filters
  totalCount: number
}

export const CatalogPagination = ({ currentFilters, totalCount }: Props) => {
  const router = useRouter()

  const page = Number(currentFilters.page) || 1
  const pageSize = Number(currentFilters.pageSize) || 24
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  const buildHref = (targetPage: number) => {
    const params = new URLSearchParams()
    params.set('page', String(targetPage))
    params.set('pageSize', currentFilters.pageSize)
    params.set('sortBy', currentFilters.sortBy)
    params.set('sortDirection', currentFilters.sortDirection)
    return `/catalog?${params.toString()}`
  }

  const goTo = (targetPage: number) => {
    if (targetPage < 1 || targetPage > totalPages || targetPage === page) return
    router.push(buildHref(targetPage))
  }

  // Build a compact list of page numbers with ellipses,
  // e.g. [1, 'ellipsis', 4, 5, 6, 'ellipsis', 10]
  const getPageNumbers = (): (number | 'ellipsis')[] => {
    const delta = 1
    const range: (number | 'ellipsis')[] = []
    const rangeWithDots: (number | 'ellipsis')[] = []
    let last: number | undefined

    for (let i = 1; i <= totalPages; i++) {
      if (i === 1 || i === totalPages || (i >= page - delta && i <= page + delta)) {
        range.push(i)
      }
    }

    for (const i of range) {
      if (typeof i === 'number') {
        if (last !== undefined) {
          if (i - last === 2) {
            rangeWithDots.push(last + 1)
          } else if (i - last > 2) {
            rangeWithDots.push('ellipsis')
          }
        }
        rangeWithDots.push(i)
        last = i
      }
    }

    return rangeWithDots
  }

  if (totalPages <= 1) return null

  return (
    <Pagination>
      <PaginationContent>
        <PaginationItem>
          <PaginationPrevious
            href={buildHref(page - 1)}
            onClick={(e) => {
              e.preventDefault()
              goTo(page - 1)
            }}
            aria-disabled={page <= 1}
            className={page <= 1 ? 'pointer-events-none opacity-50' : ''}
          />
        </PaginationItem>

        {getPageNumbers().map((item, idx) =>
          item === 'ellipsis' ? (
            <PaginationItem key={`ellipsis-${idx}`}>
              <PaginationEllipsis />
            </PaginationItem>
          ) : (
            <PaginationItem key={item}>
              <PaginationLink
                href={buildHref(item)}
                isActive={item === page}
                onClick={(e) => {
                  e.preventDefault()
                  goTo(item)
                }}
              >
                {item}
              </PaginationLink>
            </PaginationItem>
          )
        )}

        <PaginationItem>
          <PaginationNext
            href={buildHref(page + 1)}
            onClick={(e) => {
              e.preventDefault()
              goTo(page + 1)
            }}
            aria-disabled={page >= totalPages}
            className={page >= totalPages ? 'pointer-events-none opacity-50' : ''}
          />
        </PaginationItem>
      </PaginationContent>
    </Pagination>
  )
}